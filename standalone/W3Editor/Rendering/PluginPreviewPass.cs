using System.Numerics;
using Silk.NET.OpenGL;
using W3Editor.PluginContracts;

namespace W3Editor.Rendering;

/// <summary>
/// Single-slot plugin mesh preview for the editor GL viewport.
/// Mesh is in W3/d1map axes (+X east, +Y north, +Z up); a permute matrix
/// maps into editor world (+X east, +Y up, +Z north) at the given origin.
/// </summary>
public sealed class PluginPreviewPass
{
    private GL? _gl;
    private uint _program;
    private uint _vao, _vbo, _ebo, _texture;
    private int _indexCount;
    private bool _dirty;
    private bool _ownsTexture;
    private PluginMeshPreview? _pending;
    private Vector3 _origin;
    private string _label = "";

    public bool HasPreview => _pending != null || _indexCount > 0;
    public string Label => _label;

    public void Initialize(GL gl)
    {
        _gl = gl;
        _program = 0;
        _vao = _vbo = _ebo = _texture = 0;
        _indexCount = 0;
        _ownsTexture = false;
        _dirty = _pending != null;
    }

    public void Set(PluginMeshPreview mesh, string label, Vector3 originWorld)
    {
        _pending = mesh ?? new PluginMeshPreview();
        _label = label ?? "";
        _origin = originWorld;
        _dirty = true;
    }

    public void Clear()
    {
        _pending = null;
        _label = "";
        _origin = Vector3.Zero;
        _dirty = true;
        _indexCount = 0;
    }

    public void Render(
        Matrix4x4 vp,
        Vector3 lightDir,
        float ambient,
        float lightScale,
        Func<string, string, uint> compile)
    {
        if (_gl == null)
            return;
        if (_dirty)
            Upload(compile);
        if (_indexCount <= 0 || _program == 0)
            return;

        GL gl = _gl;
        gl.UseProgram(_program);
        SetMat4(gl, "u_vp", vp);
        SetMat4(gl, "u_model", BuildModelMatrix(_origin));
        gl.Uniform3(gl.GetUniformLocation(_program, "u_light_dir"), lightDir.X, lightDir.Y, lightDir.Z);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_ambient"), ambient);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_light_scale"), lightScale);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_texture"), 0);

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, _texture);
        gl.BindVertexArray(_vao);
        unsafe
        {
            gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null);
        }
        gl.BindVertexArray(0);
    }

    /// <summary>CPU-only smoke: Set/Clear with empty and tiny mesh must not throw.</summary>
    public static string SmokeTest()
    {
        var pass = new PluginPreviewPass();
        pass.Set(new PluginMeshPreview(), "empty", Vector3.Zero);
        if (pass.Label != "empty" || !pass.HasPreview)
            return "empty-set";
        pass.Clear();
        if (pass.HasPreview || pass.Label.Length != 0)
            return "clear";
        pass.Set(new PluginMeshPreview
        {
            Positions = new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f },
            Normals = new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f },
            Uv0 = new[] { 0f, 0f, 1f, 0f, 0f, 1f },
            Indices = new[] { 0, 1, 2 },
        }, "tri", new Vector3(10f, 20f, 30f));
        if (!pass.HasPreview || pass.Label != "tri")
            return "tri-set";
        pass.Clear();
        return pass.HasPreview ? "final-clear" : "pass";
    }

    private void Upload(Func<string, string, uint> compile)
    {
        GL gl = _gl!;
        if (_program == 0)
            _program = compile(VertexSource, FragmentSource);

        ReleaseGpuMesh(gl);

        PluginMeshPreview? mesh = _pending;
        _dirty = false;
        if (mesh == null)
            return;

        float[] interleaved = BuildInterleaved(mesh);
        uint[] indices = BuildIndices(mesh);
        if (interleaved.Length < 8 * 3 || indices.Length < 3)
            return;

        _vbo = gl.GenBuffer();
        _ebo = gl.GenBuffer();
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)interleaved, BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        gl.BufferData(BufferTargetARB.ElementArrayBuffer, (ReadOnlySpan<uint>)indices, BufferUsageARB.StaticDraw);

        const uint stride = 8 * sizeof(float);
        unsafe
        {
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, null);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        }
        gl.BindVertexArray(0);
        _indexCount = indices.Length;
        _texture = UploadAlbedoOrChecker(gl, mesh.AlbedoPath);
    }

    private void ReleaseGpuMesh(GL gl)
    {
        if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
        if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
        if (_ebo != 0) { gl.DeleteBuffer(_ebo); _ebo = 0; }
        if (_ownsTexture && _texture != 0)
        {
            gl.DeleteTexture(_texture);
            _texture = 0;
            _ownsTexture = false;
        }
        else
        {
            _texture = 0;
        }
        _indexCount = 0;
    }

    private uint UploadAlbedoOrChecker(GL gl, string? albedoPath)
    {
        RgbaImage image;
        if (!string.IsNullOrWhiteSpace(albedoPath))
        {
            RgbaImage? loaded = RgbaImage.LoadFile(albedoPath);
            image = loaded ?? GrayChecker();
        }
        else
        {
            image = GrayChecker();
        }

        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)image.Width, (uint)image.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (ReadOnlySpan<byte>)image.Pixels);
        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        _ownsTexture = true;
        return tex;
    }

    private static RgbaImage GrayChecker(int size = 64)
    {
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool on = ((x >> 3) + (y >> 3)) % 2 == 0;
            byte v = on ? (byte)180 : (byte)110;
            int i = (y * size + x) * 4;
            pixels[i] = v;
            pixels[i + 1] = v;
            pixels[i + 2] = v;
            pixels[i + 3] = 255;
        }
        return new RgbaImage(size, size, pixels);
    }

    /// <summary>W3 axes → editor world, then translate to map-center ground.</summary>
    private static Matrix4x4 BuildModelMatrix(Vector3 origin)
    {
        var perm = new Matrix4x4(
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);
        return perm * Matrix4x4.CreateTranslation(origin);
    }

    private static float[] BuildInterleaved(PluginMeshPreview mesh)
    {
        int vertCount = mesh.Positions.Length / 3;
        if (vertCount <= 0)
            return Array.Empty<float>();
        var dst = new float[vertCount * 8];
        for (int i = 0; i < vertCount; i++)
        {
            int po = i * 3;
            int uo = i * 2;
            int d = i * 8;
            dst[d] = mesh.Positions[po];
            dst[d + 1] = mesh.Positions[po + 1];
            dst[d + 2] = mesh.Positions[po + 2];
            if (mesh.Normals.Length >= po + 3)
            {
                dst[d + 3] = mesh.Normals[po];
                dst[d + 4] = mesh.Normals[po + 1];
                dst[d + 5] = mesh.Normals[po + 2];
            }
            else
            {
                dst[d + 3] = 0f;
                dst[d + 4] = 0f;
                dst[d + 5] = 1f;
            }
            if (mesh.Uv0.Length >= uo + 2)
            {
                dst[d + 6] = mesh.Uv0[uo];
                dst[d + 7] = mesh.Uv0[uo + 1];
            }
        }
        return dst;
    }

    private static uint[] BuildIndices(PluginMeshPreview mesh)
    {
        if (mesh.Indices.Length == 0)
            return Array.Empty<uint>();
        var dst = new uint[mesh.Indices.Length];
        for (int i = 0; i < mesh.Indices.Length; i++)
            dst[i] = (uint)Math.Max(0, mesh.Indices[i]);
        return dst;
    }

    private void SetMat4(GL gl, string name, Matrix4x4 matrix)
    {
        Span<float> data = stackalloc float[16]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        };
        gl.UniformMatrix4(gl.GetUniformLocation(_program, name), 1, false, data);
    }

    private const string VertexSource = @"
layout(location = 0) in vec3 a_position;
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_uv;
uniform mat4 u_vp;
uniform mat4 u_model;
out vec3 v_normal;
out vec2 v_uv;

void main() {
    // Numerics row-major uploaded as-is → GLSL columns == transpose,
    // so M * v equals v(row) * M(numerics). Same as GlDoodadPass.
    vec4 world = u_model * vec4(a_position, 1.0);
    v_normal = normalize(mat3(u_model) * a_normal);
    v_uv = a_uv;
    gl_Position = u_vp * world;
}
";

    private const string FragmentSource = @"
in vec3 v_normal;
in vec2 v_uv;
uniform sampler2D u_texture;
uniform vec3 u_light_dir;
uniform float u_ambient;
uniform float u_light_scale;
out vec4 o_color;

void main() {
    vec4 col = texture(u_texture, v_uv);
    float ndotl = max(dot(normalize(v_normal), normalize(u_light_dir)), 0.0);
    float light = min(1.0, mix(u_ambient, 1.0, ndotl) * u_light_scale);
    o_color = vec4(col.rgb * light, 1.0);
}
";
}
