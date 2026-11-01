using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Xilium.CefGlue.GTK.Interop
{
    /// <summary>
    /// GL function pointers resolved via eglGetProcAddress rather than a static DllImport against
    /// libGL.so.1/libGLESv2.so.2 - GtkGLArea can negotiate either a desktop GL or a GLES context
    /// depending on the platform/driver (gtk_gl_area_get_use_es), and the two live in DIFFERENT
    /// shared libraries with different sonames, so a single hardcoded DllImport target can't cover
    /// both. eglGetProcAddress resolves core entry points as well as extensions on every EGL
    /// implementation this project targets (Mesa) - the same technique any cross-platform GL
    /// function loader (GLAD etc.) uses for an EGL-backed context. Must only be called with a GL
    /// context CURRENT on the calling thread (GtkGLArea's own OnRealize/OnRender - see
    /// GtkTextureRenderSurface's remarks).
    /// </summary>
    internal static class GlInterop
    {
        public const uint GL_TEXTURE_2D = 0x0DE1;
        public const uint GL_TEXTURE0 = 0x84C0;
        public const uint GL_TEXTURE_MIN_FILTER = 0x2801;
        public const uint GL_TEXTURE_MAG_FILTER = 0x2800;
        public const int GL_LINEAR = 0x2601;
        public const uint GL_TEXTURE_WRAP_S = 0x2802;
        public const uint GL_TEXTURE_WRAP_T = 0x2803;
        public const int GL_CLAMP_TO_EDGE = 0x812F;
        public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        public const uint GL_ARRAY_BUFFER = 0x8892;
        public const uint GL_STATIC_DRAW = 0x88E4;
        public const uint GL_FLOAT = 0x1406;
        public const uint GL_TRIANGLE_STRIP = 0x0005;
        public const uint GL_VERTEX_SHADER = 0x8B31;
        public const uint GL_FRAGMENT_SHADER = 0x8B30;
        public const uint GL_COMPILE_STATUS = 0x8B81;
        public const uint GL_LINK_STATUS = 0x8B82;
        public const uint GL_BLEND = 0x0BE2;
        public const uint GL_ONE = 1;
        public const uint GL_SRC_ALPHA = 0x0302;
        public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
        public const uint GL_NO_ERROR = 0;
        public const uint GL_TEXTURE_EXTERNAL_OES = 0x8D65;

        public delegate void GenTexturesDelegate(int n, out uint textures);
        public delegate void DeleteTexturesDelegate(int n, ref uint textures);
        public delegate void BindTextureDelegate(uint target, uint texture);
        public delegate void TexParameteriDelegate(uint target, uint pname, int param);
        public delegate void ActiveTextureDelegate(uint texture);
        public delegate void ViewportDelegate(int x, int y, int width, int height);
        public delegate void ClearColorDelegate(float r, float g, float b, float a);
        public delegate void ClearDelegate(uint mask);
        public delegate void UseProgramDelegate(uint program);
        public delegate uint CreateShaderDelegate(uint type);
        public delegate void ShaderSourceDelegate(uint shader, int count, string[] str, int[] length);
        public delegate void CompileShaderDelegate(uint shader);
        public delegate void GetShaderivDelegate(uint shader, uint pname, out int param);
        public delegate void GetShaderInfoLogDelegate(uint shader, int maxLength, out int length, StringBuilder infoLog);
        public delegate uint CreateProgramDelegate();
        public delegate void AttachShaderDelegate(uint program, uint shader);
        public delegate void LinkProgramDelegate(uint program);
        public delegate void GetProgramivDelegate(uint program, uint pname, out int param);
        public delegate void GetProgramInfoLogDelegate(uint program, int maxLength, out int length, StringBuilder infoLog);
        public delegate void DeleteShaderDelegate(uint shader);
        public delegate void DeleteProgramDelegate(uint program);
        public delegate void GenBuffersDelegate(int n, out uint buffers);
        public delegate void DeleteBuffersDelegate(int n, ref uint buffers);
        public delegate void BindBufferDelegate(uint target, uint buffer);
        public delegate void BufferDataDelegate(uint target, IntPtr size, float[] data, uint usage);
        public delegate void VertexAttribPointerDelegate(uint index, int size, uint type, [MarshalAs(UnmanagedType.I1)] bool normalized, int stride, IntPtr pointer);
        public delegate void EnableVertexAttribArrayDelegate(uint index);
        public delegate void DrawArraysDelegate(uint mode, int first, int count);
        public delegate int GetUniformLocationDelegate(uint program, string name);
        public delegate void Uniform1iDelegate(int location, int value);
        public delegate void GenVertexArraysDelegate(int n, out uint arrays);
        public delegate void BindVertexArrayDelegate(uint array);
        public delegate void DeleteVertexArraysDelegate(int n, ref uint arrays);
        public delegate uint GetErrorDelegate();
        public delegate void EnableDelegate(uint cap);
        public delegate void BlendFuncDelegate(uint sfactor, uint dfactor);
        public delegate void EGLImageTargetTexture2DOESDelegate(uint target, IntPtr image);

        public static GenTexturesDelegate glGenTextures;
        public static DeleteTexturesDelegate glDeleteTextures;
        public static BindTextureDelegate glBindTexture;
        public static TexParameteriDelegate glTexParameteri;
        public static ActiveTextureDelegate glActiveTexture;
        public static ViewportDelegate glViewport;
        public static ClearColorDelegate glClearColor;
        public static ClearDelegate glClear;
        public static UseProgramDelegate glUseProgram;
        public static CreateShaderDelegate glCreateShader;
        public static ShaderSourceDelegate glShaderSource;
        public static CompileShaderDelegate glCompileShader;
        public static GetShaderivDelegate glGetShaderiv;
        public static GetShaderInfoLogDelegate glGetShaderInfoLog;
        public static CreateProgramDelegate glCreateProgram;
        public static AttachShaderDelegate glAttachShader;
        public static LinkProgramDelegate glLinkProgram;
        public static GetProgramivDelegate glGetProgramiv;
        public static GetProgramInfoLogDelegate glGetProgramInfoLog;
        public static DeleteShaderDelegate glDeleteShader;
        public static DeleteProgramDelegate glDeleteProgram;
        public static GenBuffersDelegate glGenBuffers;
        public static DeleteBuffersDelegate glDeleteBuffers;
        public static BindBufferDelegate glBindBuffer;
        public static BufferDataDelegate glBufferData;
        public static VertexAttribPointerDelegate glVertexAttribPointer;
        public static EnableVertexAttribArrayDelegate glEnableVertexAttribArray;
        public static DrawArraysDelegate glDrawArrays;
        public static GetUniformLocationDelegate glGetUniformLocation;
        public static Uniform1iDelegate glUniform1i;
        public static GenVertexArraysDelegate glGenVertexArrays;
        public static BindVertexArrayDelegate glBindVertexArray;
        public static DeleteVertexArraysDelegate glDeleteVertexArrays;
        public static GetErrorDelegate glGetError;
        public static EnableDelegate glEnable;
        public static BlendFuncDelegate glBlendFunc;
        public static EGLImageTargetTexture2DOESDelegate glEGLImageTargetTexture2DOES;

        private static bool _loaded;

        /// <summary>Must be called once with a GL context current (GtkGLArea's OnRealize).</summary>
        public static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            glGenTextures = Load<GenTexturesDelegate>("glGenTextures");
            glDeleteTextures = Load<DeleteTexturesDelegate>("glDeleteTextures");
            glBindTexture = Load<BindTextureDelegate>("glBindTexture");
            glTexParameteri = Load<TexParameteriDelegate>("glTexParameteri");
            glActiveTexture = Load<ActiveTextureDelegate>("glActiveTexture");
            glViewport = Load<ViewportDelegate>("glViewport");
            glClearColor = Load<ClearColorDelegate>("glClearColor");
            glClear = Load<ClearDelegate>("glClear");
            glUseProgram = Load<UseProgramDelegate>("glUseProgram");
            glCreateShader = Load<CreateShaderDelegate>("glCreateShader");
            glShaderSource = Load<ShaderSourceDelegate>("glShaderSource");
            glCompileShader = Load<CompileShaderDelegate>("glCompileShader");
            glGetShaderiv = Load<GetShaderivDelegate>("glGetShaderiv");
            glGetShaderInfoLog = Load<GetShaderInfoLogDelegate>("glGetShaderInfoLog");
            glCreateProgram = Load<CreateProgramDelegate>("glCreateProgram");
            glAttachShader = Load<AttachShaderDelegate>("glAttachShader");
            glLinkProgram = Load<LinkProgramDelegate>("glLinkProgram");
            glGetProgramiv = Load<GetProgramivDelegate>("glGetProgramiv");
            glGetProgramInfoLog = Load<GetProgramInfoLogDelegate>("glGetProgramInfoLog");
            glDeleteShader = Load<DeleteShaderDelegate>("glDeleteShader");
            glDeleteProgram = Load<DeleteProgramDelegate>("glDeleteProgram");
            glGenBuffers = Load<GenBuffersDelegate>("glGenBuffers");
            glDeleteBuffers = Load<DeleteBuffersDelegate>("glDeleteBuffers");
            glBindBuffer = Load<BindBufferDelegate>("glBindBuffer");
            glBufferData = Load<BufferDataDelegate>("glBufferData");
            glVertexAttribPointer = Load<VertexAttribPointerDelegate>("glVertexAttribPointer");
            glEnableVertexAttribArray = Load<EnableVertexAttribArrayDelegate>("glEnableVertexAttribArray");
            glDrawArrays = Load<DrawArraysDelegate>("glDrawArrays");
            glGetUniformLocation = Load<GetUniformLocationDelegate>("glGetUniformLocation");
            glUniform1i = Load<Uniform1iDelegate>("glUniform1i");
            glGenVertexArrays = Load<GenVertexArraysDelegate>("glGenVertexArrays");
            glBindVertexArray = Load<BindVertexArrayDelegate>("glBindVertexArray");
            glDeleteVertexArrays = Load<DeleteVertexArraysDelegate>("glDeleteVertexArrays");
            glGetError = Load<GetErrorDelegate>("glGetError");
            glEnable = Load<EnableDelegate>("glEnable");
            glBlendFunc = Load<BlendFuncDelegate>("glBlendFunc");
            // OES extension - genuinely has no static export anywhere, ALWAYS resolved dynamically
            // even on desktop GL (unlike the core functions above, which are only resolved this way
            // for portability - see class remarks).
            glEGLImageTargetTexture2DOES = Load<EGLImageTargetTexture2DOESDelegate>("glEGLImageTargetTexture2DOES");

            _loaded = true;
        }

        private static T Load<T>(string name) where T : Delegate
        {
            var ptr = EglInterop.eglGetProcAddress(name);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"eglGetProcAddress(\"{name}\") returned NULL - required GL/EGL entry point not available on this driver.");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }
    }
}
