using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gtk;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.GTK.Interop;

namespace Xilium.CefGlue.GTK
{
    /// <summary>
    /// True GPU-passthrough OSR render surface for GTK, drawn via a real GtkGLArea + GL texture +
    /// shader quad - the direct architectural analog of CefGlue.WinUI's WinUITextureRenderSurface
    /// (SwapChainPanel + D3D11 shared texture): CEF's dma-buf is imported as an EGLImage
    /// (EGL_EXT_image_dma_buf_import, EGL_NO_CONTEXT since this target imports a memory object, not
    /// a live GL object from a source context), bound to a GL texture via
    /// glEGLImageTargetTexture2DOES, and drawn into GtkGLArea's own framebuffer on every "render"
    /// signal - a real OpenGL/GLES draw call pipeline end to end, not GSK's scene-graph compositing
    /// (see CefGlue.GTK.Common.TextureControl's own remarks for why this was chosen over the
    /// simpler Gtk.Picture+Gdk.DmabufTexture path that GtkRenderSurface's CPU path and this
    /// project's earlier accelerated-paint experiments already used).
    ///
    /// No CPU pixel copy anywhere in this class - eglCreateImageKHR takes only the dma-buf's file
    /// descriptors + stride/offset/modifier metadata, never touches the pixel data itself; the
    /// resulting texture is sampled directly by the GPU during the shader draw call.
    ///
    /// UNPROVEN, needs live Linux testing (cannot be verified from this dev environment):
    /// - Whether GDK's GL context is itself EGL-backed on the target windowing system (X11 can
    ///   still default to GLX in some GTK/driver combinations) - eglGetCurrentDisplay/
    ///   eglGetCurrentContext only return valid handles if it is. If GDK ever uses GLX here, this
    ///   whole approach needs a GLX_EXT_texture_from_pixmap-based path instead - a different
    ///   extension entirely.
    /// - Texture V-axis orientation (top-first vs bottom-first) - the quad's texcoord mapping below
    ///   is the common convention used by Weston/wlroots/GStreamer's own EGL dma-buf import code,
    ///   but CEF's own row order for this export wasn't independently confirmed here. If the frame
    ///   renders upside-down, swap the v components (0↔1) in QuadVertices.
    /// - Whether the modifier value CEF reports (confirmed elsewhere in this project, GtkRenderSurface's
    ///   own remarks, to often be DRM_FORMAT_MOD_INVALID rather than a real modifier) is accepted as-is
    ///   by eglCreateImageKHR's PLANE*_MODIFIER_LO/HI attribs on this driver - passed through unmodified
    ///   here since that's what the already-working Gdk.DmabufTextureBuilder path does with the same
    ///   value (see DeliverAcceleratedFrame's own remarks).
    /// </summary>
    internal sealed class GtkTextureRenderSurface : OffScreenRenderSurface
    {
        private const uint DrmFormatArgb8888 = 0x34325241; // fourcc('A','R','2','4') - CefColorType.Bgra8888
        private const uint DrmFormatAbgr8888 = 0x34324241; // fourcc('A','B','2','4') - CefColorType.Rgba8888

        // pos.xy, texcoord.uv - GL_TRIANGLE_STRIP order (BL, BR, TL, TR). See class remarks on
        // texcoord V-axis orientation being unproven without live testing.
        private static readonly float[] QuadVertices =
        {
            -1f, -1f,   0f, 1f,
             1f, -1f,   1f, 1f,
            -1f,  1f,   0f, 0f,
             1f,  1f,   1f, 0f,
        };

        // Guards _glArea/_textureId/_disposed against the UI-thread-close-vs-CEF-paint-thread race,
        // same rationale as WinUITextureRenderSurface's/AvaloniaTextureRenderSurface's _deviceLock
        // and this class's own Picture-based sibling's _pictureLock.
        private readonly object _glLock = new object();
        private GLArea _glArea;
        private bool _disposed;

        private uint _program;
        private uint _vao;
        private uint _vbo;
        private uint _textureId;
        private int _textureUniformLocation;
        private bool _useEs;

        // Captured on construction (GTK main thread) - every Gtk/Gdk/GL/EGL call in this class MUST
        // run on this context, never on whatever thread CEF's OnAcceleratedPaint callback fired on
        // (GTK/GDK/GL are not thread-safe - confirmed live elsewhere in this codebase).
        private readonly SynchronizationContext _uiContext = SynchronizationContext.Current;

        public int CurrentWidth { get; private set; }

        public int CurrentHeight { get; private set; }

        public override bool AllowsTransparency => true;

        /// <summary>Called once by TextureControl when its GtkGLArea is ready.</summary>
        public void AttachGLArea(GLArea glArea)
        {
            lock (_glLock)
            {
                _glArea = glArea;
            }

            glArea.OnRealize += (_, _) => InitializeGL();
            glArea.OnUnrealize += (_, _) => CleanupGL();
            glArea.OnRender += (_, _) =>
            {
                DrawFrame();
                return true;
            };
        }

        private void InitializeGL()
        {
            _glArea.MakeCurrent();
            var error = _glArea.GetError();
            if (error != null)
            {
                // Can't render - _program/_textureId stay 0, DrawFrame's own guard skips drawing
                // rather than crashing on unusable GL resources.
                return;
            }

            GlInterop.EnsureLoaded();

            _useEs = _glArea.GetUseEs();
            var (vertexSource, fragmentSource) = BuildShaderSource(_useEs);

            var vertexShader = CompileShader(GlInterop.GL_VERTEX_SHADER, vertexSource);
            var fragmentShader = CompileShader(GlInterop.GL_FRAGMENT_SHADER, fragmentSource);
            _program = LinkProgram(vertexShader, fragmentShader);
            GlInterop.glDeleteShader(vertexShader);
            GlInterop.glDeleteShader(fragmentShader);
            _textureUniformLocation = GlInterop.glGetUniformLocation(_program, "uTexture");

            GlInterop.glGenVertexArrays(1, out _vao);
            GlInterop.glGenBuffers(1, out _vbo);
            GlInterop.glBindVertexArray(_vao);
            GlInterop.glBindBuffer(GlInterop.GL_ARRAY_BUFFER, _vbo);
            GlInterop.glBufferData(GlInterop.GL_ARRAY_BUFFER, (IntPtr)(QuadVertices.Length * sizeof(float)), QuadVertices, GlInterop.GL_STATIC_DRAW);
            GlInterop.glVertexAttribPointer(0, 2, GlInterop.GL_FLOAT, false, 4 * sizeof(float), IntPtr.Zero);
            GlInterop.glEnableVertexAttribArray(0);
            GlInterop.glVertexAttribPointer(1, 2, GlInterop.GL_FLOAT, false, 4 * sizeof(float), (IntPtr)(2 * sizeof(float)));
            GlInterop.glEnableVertexAttribArray(1);
            GlInterop.glBindVertexArray(0);

            GlInterop.glGenTextures(1, out _textureId);
            GlInterop.glBindTexture(GlInterop.GL_TEXTURE_2D, _textureId);
            GlInterop.glTexParameteri(GlInterop.GL_TEXTURE_2D, GlInterop.GL_TEXTURE_MIN_FILTER, GlInterop.GL_LINEAR);
            GlInterop.glTexParameteri(GlInterop.GL_TEXTURE_2D, GlInterop.GL_TEXTURE_MAG_FILTER, GlInterop.GL_LINEAR);
            GlInterop.glTexParameteri(GlInterop.GL_TEXTURE_2D, GlInterop.GL_TEXTURE_WRAP_S, GlInterop.GL_CLAMP_TO_EDGE);
            GlInterop.glTexParameteri(GlInterop.GL_TEXTURE_2D, GlInterop.GL_TEXTURE_WRAP_T, GlInterop.GL_CLAMP_TO_EDGE);
        }

        private void CleanupGL()
        {
            try
            {
                _glArea.MakeCurrent();
            }
            catch
            {
                // Widget's native surface may already be gone by the time unrealize runs during
                // teardown - nothing left to make current, nothing left to clean up either.
                return;
            }

            lock (_glLock)
            {
                if (_textureId != 0) { GlInterop.glDeleteTextures(1, ref _textureId); _textureId = 0; }
                if (_vbo != 0) { GlInterop.glDeleteBuffers(1, ref _vbo); _vbo = 0; }
                if (_vao != 0) { GlInterop.glDeleteVertexArrays(1, ref _vao); _vao = 0; }
                if (_program != 0) { GlInterop.glDeleteProgram(_program); _program = 0; }
            }
        }

        private void DrawFrame()
        {
            GlInterop.glClearColor(0f, 0f, 0f, 0f);
            GlInterop.glClear(GlInterop.GL_COLOR_BUFFER_BIT);

            if (_program == 0 || _textureId == 0 || CurrentWidth <= 0)
            {
                return;
            }

            // CEF's accelerated buffers are premultiplied (mirrors the Picture-based sibling's own
            // DmabufTextureBuilder.Premultiplied = true) - GL_ONE/GL_ONE_MINUS_SRC_ALPHA is the
            // correct premultiplied-over blend, not the more commonly seen GL_SRC_ALPHA/
            // GL_ONE_MINUS_SRC_ALPHA (that pairing is for STRAIGHT/non-premultiplied alpha).
            GlInterop.glEnable(GlInterop.GL_BLEND);
            GlInterop.glBlendFunc(GlInterop.GL_ONE, GlInterop.GL_ONE_MINUS_SRC_ALPHA);

            GlInterop.glUseProgram(_program);
            GlInterop.glActiveTexture(GlInterop.GL_TEXTURE0);
            GlInterop.glBindTexture(GlInterop.GL_TEXTURE_2D, _textureId);
            GlInterop.glUniform1i(_textureUniformLocation, 0);
            GlInterop.glBindVertexArray(_vao);
            GlInterop.glDrawArrays(GlInterop.GL_TRIANGLE_STRIP, 0, 4);
        }

        private static string BuildShaderSource(bool useEs, bool vertex)
        {
            var header = useEs ? "#version 300 es\nprecision mediump float;\n" : "#version 150\n";
            return header + (vertex
                ? "in vec2 aPos;\nin vec2 aTexCoord;\nout vec2 vTexCoord;\nvoid main() {\n    gl_Position = vec4(aPos, 0.0, 1.0);\n    vTexCoord = aTexCoord;\n}\n"
                : "in vec2 vTexCoord;\nout vec4 fragColor;\nuniform sampler2D uTexture;\nvoid main() {\n    fragColor = texture(uTexture, vTexCoord);\n}\n");
        }

        private static (string vertex, string fragment) BuildShaderSource(bool useEs)
            => (BuildShaderSource(useEs, vertex: true), BuildShaderSource(useEs, vertex: false));

        private static uint CompileShader(uint type, string source)
        {
            var shader = GlInterop.glCreateShader(type);
            GlInterop.glShaderSource(shader, 1, new[] { source }, new[] { source.Length });
            GlInterop.glCompileShader(shader);
            GlInterop.glGetShaderiv(shader, GlInterop.GL_COMPILE_STATUS, out var status);
            if (status == 0)
            {
                var log = new StringBuilder(2048);
                GlInterop.glGetShaderInfoLog(shader, 2048, out _, log);
                GlInterop.glDeleteShader(shader);
                throw new InvalidOperationException($"GL shader compile failed: {log}");
            }
            return shader;
        }

        private static uint LinkProgram(uint vertexShader, uint fragmentShader)
        {
            var program = GlInterop.glCreateProgram();
            GlInterop.glAttachShader(program, vertexShader);
            GlInterop.glAttachShader(program, fragmentShader);
            GlInterop.glLinkProgram(program);
            GlInterop.glGetProgramiv(program, GlInterop.GL_LINK_STATUS, out var status);
            if (status == 0)
            {
                var log = new StringBuilder(2048);
                GlInterop.glGetProgramInfoLog(program, 2048, out _, log);
                GlInterop.glDeleteProgram(program);
                throw new InvalidOperationException($"GL program link failed: {log}");
            }
            return program;
        }

        protected override Task ExecuteInUIThread(Action action)
        {
            if (_uiContext == null)
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource();

            _uiContext.Post(_ =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        protected override (int width, int height) GetAcceleratedFrameSize(CefAcceleratedPaintInfo info)
            => (info.Extra.CodedSize.Width, info.Extra.CodedSize.Height);

        protected override void DeliverSoftwareFrame(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects)
        {
            // This surface only exists for the accelerated/shared-texture path (see
            // CefGlueBrowser.ResolveControl - TextureControl is only selected when
            // SharedTextureEnabled is on) - CEF should never call OnPaint for a browser configured
            // this way, so there's nothing to do here. Mirrors WinUITextureRenderSurface's identical
            // no-op.
        }

        protected override void DeliverAcceleratedFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects)
        {
            lock (_glLock)
            {
                if (_disposed || _glArea == null || _textureId == 0)
                {
                    return;
                }

                var width = info.Extra.CodedSize.Width;
                var height = info.Extra.CodedSize.Height;

                if (width <= 0 || height <= 0)
                {
                    return;
                }

                _glArea.MakeCurrent();

                var fourcc = info.Format == CefColorType.Rgba8888 ? DrmFormatAbgr8888 : DrmFormatArgb8888;

                var attribs = new List<int>
                {
                    EglInterop.EGL_WIDTH, width,
                    EglInterop.EGL_HEIGHT, height,
                    EglInterop.EGL_LINUX_DRM_FOURCC_EXT, unchecked((int)fourcc),
                };

                for (var i = 0; i < info.PlaneCount && i < 4; i++)
                {
                    AppendPlaneAttribs(attribs, i, info);
                }

                attribs.Add(EglInterop.EGL_NONE);

                var display = EglInterop.eglGetCurrentDisplay();
                var image = EglInterop.eglCreateImageKHR(display, EglInterop.EGL_NO_CONTEXT, EglInterop.EGL_LINUX_DMA_BUF_EXT, IntPtr.Zero, attribs.ToArray());

                if (image == EglInterop.EGL_NO_IMAGE_KHR)
                {
                    // Drop this frame rather than throw - a single failed import (e.g. a transient
                    // driver hiccup, or an unsupported modifier) shouldn't take the whole render
                    // pipeline down; the next OnAcceleratedPaint callback gets another chance.
                    return;
                }

                GlInterop.glBindTexture(GlInterop.GL_TEXTURE_2D, _textureId);
                GlInterop.glEGLImageTargetTexture2DOES(GlInterop.GL_TEXTURE_2D, image);

                // Safe to destroy immediately after binding - the driver takes its own reference to
                // the underlying dma-buf via glEGLImageTargetTexture2DOES, same idiom used by
                // Weston/wlroots/GStreamer's own EGL dma-buf import paths. Deliberately never calling
                // gdk_texture_download or routing through GSK at all - the whole point of this class.
                EglInterop.eglDestroyImageKHR(display, image);

                CurrentWidth = width;
                CurrentHeight = height;
            }

            _glArea.QueueRender();
        }

        private static void AppendPlaneAttribs(List<int> attribs, int planeIndex, CefAcceleratedPaintInfo info)
        {
            var plane = info.Planes[planeIndex];
            var (fdKey, offsetKey, pitchKey, modLoKey, modHiKey) = planeIndex switch
            {
                0 => (EglInterop.EGL_DMA_BUF_PLANE0_FD_EXT, EglInterop.EGL_DMA_BUF_PLANE0_OFFSET_EXT, EglInterop.EGL_DMA_BUF_PLANE0_PITCH_EXT, EglInterop.EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT, EglInterop.EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT),
                1 => (EglInterop.EGL_DMA_BUF_PLANE1_FD_EXT, EglInterop.EGL_DMA_BUF_PLANE1_OFFSET_EXT, EglInterop.EGL_DMA_BUF_PLANE1_PITCH_EXT, EglInterop.EGL_DMA_BUF_PLANE1_MODIFIER_LO_EXT, EglInterop.EGL_DMA_BUF_PLANE1_MODIFIER_HI_EXT),
                2 => (EglInterop.EGL_DMA_BUF_PLANE2_FD_EXT, EglInterop.EGL_DMA_BUF_PLANE2_OFFSET_EXT, EglInterop.EGL_DMA_BUF_PLANE2_PITCH_EXT, EglInterop.EGL_DMA_BUF_PLANE2_MODIFIER_LO_EXT, EglInterop.EGL_DMA_BUF_PLANE2_MODIFIER_HI_EXT),
                _ => (EglInterop.EGL_DMA_BUF_PLANE3_FD_EXT, EglInterop.EGL_DMA_BUF_PLANE3_OFFSET_EXT, EglInterop.EGL_DMA_BUF_PLANE3_PITCH_EXT, EglInterop.EGL_DMA_BUF_PLANE3_MODIFIER_LO_EXT, EglInterop.EGL_DMA_BUF_PLANE3_MODIFIER_HI_EXT),
            };

            attribs.Add(fdKey); attribs.Add(plane.FileDescriptor);
            attribs.Add(offsetKey); attribs.Add(unchecked((int)plane.Offset));
            attribs.Add(pitchKey); attribs.Add(unchecked((int)plane.Stride));
            attribs.Add(modLoKey); attribs.Add(unchecked((int)(info.Modifier & 0xFFFFFFFFUL)));
            attribs.Add(modHiKey); attribs.Add(unchecked((int)(info.Modifier >> 32)));
        }

        public override void Dispose()
        {
            lock (_glLock)
            {
                _disposed = true;
            }

            _uiContext?.Post(_ =>
            {
                if (_glArea == null)
                {
                    return;
                }

                try
                {
                    CleanupGL();
                }
                catch
                {
                    // Best-effort - the widget/window may already be torn down by the time this
                    // posted callback runs.
                }
            }, null);

            base.Dispose();
        }
    }
}
