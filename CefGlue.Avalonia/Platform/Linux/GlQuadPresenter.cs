using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.OpenGL.Egl;
using Avalonia.Threading;

namespace Xilium.CefGlue.Avalonia.Platform.Linux
{
    /// <summary>
    /// Imports CEF's dma-buf frames via EGL_EXT_image_dma_buf_import and draws them as a textured quad.
    /// Avalonia's composition GPU interop has no dma-buf/EGL handle type, so this goes through
    /// OpenGlControlBase instead. Needs Avalonia's GL context to be EGL, not GLX: the app must put
    /// X11RenderingMode.Egl first in X11PlatformOptions.RenderingMode.
    /// </summary>
    internal sealed class GlQuadPresenter : OpenGlControlBase
    {
        private const int EGL_NONE = 0x3038;
        private const int EGL_WIDTH = 0x3057;
        private const int EGL_HEIGHT = 0x3056;
        private const int EGL_LINUX_DMA_BUF_EXT = 0x3270;
        private const int EGL_LINUX_DRM_FOURCC_EXT = 0x3271;
        private const int EGL_DMA_BUF_PLANE0_FD_EXT = 0x3272;
        private const int EGL_DMA_BUF_PLANE0_OFFSET_EXT = 0x3273;
        private const int EGL_DMA_BUF_PLANE0_PITCH_EXT = 0x3274;
        private const int EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT = 0x3443;
        private const int EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT = 0x3444;

        private const int GL_TEXTURE_2D = 0x0DE1;
        private const int GL_TEXTURE0 = 0x84C0;
        private const int GL_FRAMEBUFFER = 0x8D40;
        private const int GL_COLOR_BUFFER_BIT = 0x4000;
        private const int GL_BLEND = 0x0BE2;
        private const int GL_TRIANGLE_STRIP = 5;
        private const int GL_ARRAY_BUFFER = 0x8892;
        private const int GL_STATIC_DRAW = 0x88E4;
        private const int GL_FLOAT = 0x1406;
        private const int GL_TEXTURE_MIN_FILTER = 0x2801;
        private const int GL_TEXTURE_MAG_FILTER = 0x2800;
        private const int GL_LINEAR = 0x2601;
        private const int GL_TEXTURE_WRAP_S = 0x2802;
        private const int GL_TEXTURE_WRAP_T = 0x2803;
        private const int GL_CLAMP_TO_EDGE = 0x812F;
        private const int GL_VERTEX_SHADER = 0x8B31;
        private const int GL_FRAGMENT_SHADER = 0x8B30;

        // pos.xy, texcoord.uv in GL_TRIANGLE_STRIP order (BL, BR, TL, TR); CEF's rows are top-first.
        private static readonly float[] QuadVertices =
        {
            -1f, -1f,   0f, 1f,
             1f, -1f,   1f, 1f,
            -1f,  1f,   0f, 0f,
             1f,  1f,   1f, 0f,
        };

        // uTransform = (scale.xy, offset.xy) in NDC: (1, 1, 0, 0) for the view, the popup rect for the popup.
        private const string VertexShader =
            "#version 300 es\n" +
            "in vec2 aPos;\nin vec2 aTexCoord;\nout vec2 vTexCoord;\nuniform vec4 uTransform;\n" +
            "void main() {\n    gl_Position = vec4(aPos * uTransform.xy + uTransform.zw, 0.0, 1.0);\n    vTexCoord = aTexCoord;\n}\n";

        private const string FragmentShader =
            "#version 300 es\nprecision mediump float;\n" +
            "in vec2 vTexCoord;\nout vec4 fragColor;\nuniform sampler2D uTexture;\n" +
            "void main() {\n    fragColor = texture(uTexture, vTexCoord);\n}\n";

        private AvaloniaGlTextureRenderSurface _renderSurface;
        private EglInterface _egl;
        private bool _failed;

        private int _program;
        private int _vao;
        private int _vbo;
        private int _textureId;
        private int _textureUniformLocation;
        private int _transformUniformLocation;

        // The <select> popup: CEF paints it as its own dma-buf frames, drawn as a second quad over the view.
        private readonly object _popupLock = new object();
        private AvaloniaGlTextureRenderSurface _popupSurface;
        private int _popupTextureId;
        private bool _popupHasFrame;
        private bool _popupVisible;
        private Rect _popupRect;

        /// <summary>Raised on the UI thread after each draw.</summary>
        public event Action FrameRendered;

        /// <summary>Raised once, on the UI thread, when frames can't be imported at all (e.g. a GLX context).</summary>
        public event Action<string> Failed;

        public void AttachRenderSurface(AvaloniaGlTextureRenderSurface renderSurface)
        {
            _renderSurface = renderSurface;
            renderSurface.FrameReady += OnFrameReady;
        }

        public void DetachRenderSurface()
        {
            if (_renderSurface != null)
            {
                _renderSurface.FrameReady -= OnFrameReady;
                _renderSurface = null;
            }

            if (_popupSurface != null)
            {
                _popupSurface.FrameReady -= OnFrameReady;
                _popupSurface = null;
            }
        }

        public void AttachPopupSurface(AvaloniaGlTextureRenderSurface popupSurface)
        {
            _popupSurface = popupSurface;
            popupSurface.FrameReady += OnFrameReady;
        }

        /// <summary>Popup visibility and position in view DIPs; callable from any thread.</summary>
        public void SetPopup(bool visible, Rect rect)
        {
            lock (_popupLock)
            {
                _popupVisible = visible;
                _popupRect = rect;
                if (!visible)
                {
                    // Don't flash the previous popup's content when the next one opens.
                    _popupHasFrame = false;
                }
            }

            OnFrameReady();
        }

        // CEF thread; RequestNextFrameRendering has to run on the UI thread.
        private void OnFrameReady() => Dispatcher.UIThread.Post(RequestNextFrameRendering);

        protected override void OnOpenGlInit(GlInterface gl)
        {
            base.OnOpenGlInit(gl);

            try
            {
                _egl = new EglInterface();
            }
            catch (Exception ex)
            {
                Fail($"libEGL could not be loaded: {ex.Message}");
                return;
            }

            if (_egl.GetCurrentDisplay() == IntPtr.Zero)
            {
                Fail("Avalonia's OpenGL context is not EGL (GLX?). Put X11RenderingMode.Egl first in X11PlatformOptions.RenderingMode.");
                return;
            }

            if (!_egl.IsCreateImageKHRAvailable || !_egl.IsDestroyImageKHRAvailable)
            {
                Fail("EGL has no eglCreateImageKHR / eglDestroyImageKHR.");
                return;
            }

            var vertexShader = gl.CreateShader(GL_VERTEX_SHADER);
            gl.ShaderSourceString(vertexShader, VertexShader);
            gl.CompileShader(vertexShader);

            var fragmentShader = gl.CreateShader(GL_FRAGMENT_SHADER);
            gl.ShaderSourceString(fragmentShader, FragmentShader);
            gl.CompileShader(fragmentShader);

            _program = gl.CreateProgram();
            gl.AttachShader(_program, vertexShader);
            gl.AttachShader(_program, fragmentShader);
            gl.BindAttribLocationString(_program, 0, "aPos");
            gl.BindAttribLocationString(_program, 1, "aTexCoord");
            gl.LinkProgram(_program);

            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);

            _textureUniformLocation = gl.GetUniformLocationString(_program, "uTexture");
            _transformUniformLocation = gl.GetUniformLocationString(_program, "uTransform");

            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);

            _vbo = gl.GenBuffer();
            gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
            unsafe
            {
                fixed (float* p = QuadVertices)
                {
                    gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)(QuadVertices.Length * sizeof(float)), (IntPtr)p, GL_STATIC_DRAW);
                }
            }
            gl.VertexAttribPointer(0, 2, GL_FLOAT, 0, 4 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 2, GL_FLOAT, 0, 4 * sizeof(float), (IntPtr)(2 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
            gl.BindVertexArray(0);

            _textureId = CreateTexture(gl);
            _popupTextureId = CreateTexture(gl);
        }

        private static int CreateTexture(GlInterface gl)
        {
            var texture = gl.GenTexture();
            gl.BindTexture(GL_TEXTURE_2D, texture);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            return texture;
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            if (_textureId != 0) { gl.DeleteTexture(_textureId); _textureId = 0; }
            if (_popupTextureId != 0) { gl.DeleteTexture(_popupTextureId); _popupTextureId = 0; }
            if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
            if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
            if (_program != 0) { gl.DeleteProgram(_program); _program = 0; }

            base.OnOpenGlDeinit(gl);
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            var hasFrame = ImportPendingFrame(gl, _renderSurface, _textureId);
            var popupImported = ImportPendingFrame(gl, _popupSurface, _popupTextureId);

            bool drawPopup;
            Rect popupRect;
            lock (_popupLock)
            {
                // Kept even while not yet visible: CEF may paint before Open() lands, and a static popup won't repaint.
                if (popupImported)
                {
                    _popupHasFrame = true;
                }
                drawPopup = _popupVisible && _popupHasFrame;
                popupRect = _popupRect;
            }

            // OpenGlControlBase doesn't set the viewport; the framebuffer is in physical pixels.
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            gl.Viewport(0, 0, Math.Max(1, (int)(Bounds.Width * scaling)), Math.Max(1, (int)(Bounds.Height * scaling)));

            gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
            gl.ClearColor(0f, 0f, 0f, 0f);
            gl.Clear(GL_COLOR_BUFFER_BIT);

            if (_program == 0 || _textureId == 0)
            {
                return;
            }

            gl.Disable(GL_BLEND);
            gl.UseProgram(_program);
            gl.ActiveTexture(GL_TEXTURE0);
            gl.BindTexture(GL_TEXTURE_2D, _textureId);
            gl.Uniform1i(_textureUniformLocation, 0);
            gl.BindVertexArray(_vao);
            Uniform4f(gl, 1f, 1f, 0f, 0f);
            gl.DrawArrays(GL_TRIANGLE_STRIP, 0, (IntPtr)4);

            if (drawPopup && Bounds.Width > 0 && Bounds.Height > 0)
            {
                // View DIPs -> NDC (y up).
                var left = popupRect.X / Bounds.Width * 2 - 1;
                var right = popupRect.Right / Bounds.Width * 2 - 1;
                var top = 1 - popupRect.Y / Bounds.Height * 2;
                var bottom = 1 - popupRect.Bottom / Bounds.Height * 2;

                gl.BindTexture(GL_TEXTURE_2D, _popupTextureId);
                Uniform4f(gl, (float)(right - left) / 2, (float)(top - bottom) / 2, (float)(right + left) / 2, (float)(top + bottom) / 2);
                gl.DrawArrays(GL_TRIANGLE_STRIP, 0, (IntPtr)4);
            }

            if (hasFrame || popupImported)
            {
                FrameRendered?.Invoke();
            }
        }

        // GlInterface has no glUniform4f wrapper.
        private delegate void Uniform4fProc(int location, float x, float y, float z, float w);
        private Uniform4fProc _uniform4f;

        private void Uniform4f(GlInterface gl, float x, float y, float z, float w)
        {
            _uniform4f ??= System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<Uniform4fProc>(gl.GetProcAddress("glUniform4f"));
            _uniform4f(_transformUniformLocation, x, y, z, w);
        }

        private bool ImportPendingFrame(GlInterface gl, AvaloniaGlTextureRenderSurface surface, int textureId)
        {
            var frame = surface?.ConsumePendingFrame();
            if (frame == null)
            {
                return false;
            }

            try
            {
                var display = _failed ? IntPtr.Zero : _egl.GetCurrentDisplay();
                if (display == IntPtr.Zero)
                {
                    return false;
                }

                // CEF's OSR frames are a single packed BGRA/RGBA plane.
                var attribs = new[]
                {
                    EGL_WIDTH, frame.Width,
                    EGL_HEIGHT, frame.Height,
                    EGL_LINUX_DRM_FOURCC_EXT, unchecked((int)frame.FourCc),
                    EGL_DMA_BUF_PLANE0_FD_EXT, frame.Planes[0].FileDescriptor,
                    EGL_DMA_BUF_PLANE0_OFFSET_EXT, unchecked((int)frame.Planes[0].Offset),
                    EGL_DMA_BUF_PLANE0_PITCH_EXT, unchecked((int)frame.Planes[0].Stride),
                    EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT, unchecked((int)(frame.Modifier & 0xFFFFFFFFUL)),
                    EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT, unchecked((int)(frame.Modifier >> 32)),
                    EGL_NONE,
                };

                var image = _egl.CreateImageKHR(display, IntPtr.Zero, EGL_LINUX_DMA_BUF_EXT, IntPtr.Zero, attribs);
                if (image == IntPtr.Zero)
                {
                    // Drop this frame; the next one gets another chance.
                    return false;
                }

                gl.BindTexture(GL_TEXTURE_2D, textureId);
                gl.EGLImageTargetTexture2DOES(GL_TEXTURE_2D, image);

                // The texture keeps its own reference to the dma-buf.
                _egl.DestroyImageKHR(display, image);
                return true;
            }
            finally
            {
                AvaloniaGlTextureRenderSurface.CloseFrame(frame);
            }
        }

        private void Fail(string reason)
        {
            if (_failed)
            {
                return;
            }

            _failed = true;
            Failed?.Invoke(reason);
        }
    }
}
