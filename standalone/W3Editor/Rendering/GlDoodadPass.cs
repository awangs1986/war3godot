using System.Numerics;
using Silk.NET.OpenGL;
using W3Editor.Formats;
using W3Editor.Services;

namespace W3Editor.Rendering;

/// <summary>
/// Instanced doodad/destructible/unit renderer for the editor GL viewport.
/// One "bank" per (model, replaceable-texture) pair; geosets are merged per
/// (texture, blend-mode) surface and drawn with glDrawElementsInstanced using
/// a per-instance mat4 + tint (surpasses HiveWE's per-instance draws).
/// Units render statically in bind pose; unresolvable models get a
/// team-colored placeholder box.
/// </summary>
public sealed class GlDoodadPass
{
    private const int SurfaceOpaque = 0;   // filter 0
    private const int SurfaceCutout = 1;   // filter 1 / tree-leaf textures
    private const int SurfaceBlend = 2;    // filter 2
    private const int SurfaceAdditive = 3; // filter 3+

    private sealed class Surface
    {
        public uint Vao, GhostVao, Vbo, Ebo;
        public int IndexCount;
        public uint Texture;
        public int Mode;
    }

    private sealed class Bank
    {
        public readonly List<Surface> Surfaces = new();
        public uint InstanceVbo;
        public int InstanceCapacity;
        public int InstanceCount;
        public readonly List<W3Placement> Members = new();
        public float RadiusW3, MinZW3, MaxZW3; // model bounds, W3 units
        public bool IsPlaceholder;
    }

    private static readonly Vector3[] PlayerColors =
    {
        new(1.00f, 0.16f, 0.16f), new(0.00f, 0.26f, 1.00f), new(0.10f, 0.90f, 0.72f),
        new(0.33f, 0.00f, 0.51f), new(1.00f, 0.99f, 0.00f), new(1.00f, 0.54f, 0.05f),
        new(0.12f, 0.75f, 0.00f), new(0.90f, 0.36f, 0.68f), new(0.58f, 0.58f, 0.71f),
        new(0.49f, 0.80f, 0.94f), new(0.06f, 0.38f, 0.27f), new(0.31f, 0.16f, 0.02f),
    };

    private GL? _gl;
    private uint _program;
    private uint _whiteTexture;
    private uint _ghostInstanceVbo;
    private readonly Dictionary<string, Bank> _banks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MdxStaticModel?> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<W3Placement, (Bank Bank, Matrix4x4 World, Vector3 Center)> _instances = new();

    private IReadOnlyList<W3Placement> _doodads = Array.Empty<W3Placement>();
    private IReadOnlyList<W3Placement> _units = Array.Empty<W3Placement>();
    private IObjectCatalog? _catalog;
    private EditorAssets? _assets;
    private Func<float, float, float>? _groundHeight; // world X,Z -> world Y
    private bool _dirty;

    public bool ShowDoodads = true;
    public bool ShowUnits = true;
    public int RenderedCount { get; private set; }
    public int MissingModelCount { get; private set; }

    /// <summary>Selected placements get a gold highlight redraw.</summary>
    public IReadOnlyCollection<W3Placement> Selection = Array.Empty<W3Placement>();
    /// <summary>Placement ghost under the cursor while armed (null = hidden).</summary>
    public W3Placement? Ghost;

    public void SetData(
        IReadOnlyList<W3Placement> doodads, IReadOnlyList<W3Placement> units,
        IObjectCatalog catalog, EditorAssets assets, Func<float, float, float> groundHeightWorld)
    {
        _doodads = doodads;
        _units = units;
        _catalog = catalog;
        _assets = assets;
        _groundHeight = groundHeightWorld;
        _dirty = true;
    }

    public void Clear()
    {
        _doodads = Array.Empty<W3Placement>();
        _units = Array.Empty<W3Placement>();
        _instances.Clear();
        _dirty = true;
    }

    /// <summary>Instance transforms need rebuilding (placement or terrain-height change).</summary>
    public void MarkDirty() => _dirty = true;

    // ------------------------------------------------------------------
    // World transform (mirrors w3_doo_parser + w3_mdx_batcher)
    // ------------------------------------------------------------------

    /// <summary>Editor world matrix for a placement (row-vector System.Numerics convention).</summary>
    public Matrix4x4 WorldMatrix(W3Placement p, float modelScale)
    {
        float groundY = _groundHeight?.Invoke(p.X, p.Y) ?? 0f;
        float posY = p.Z < groundY + 8f ? groundY : p.Z;
        // W3 (x,y,z) → editor (x,z,y): swap Y/Z, then scale/rotate/translate.
        var perm = new Matrix4x4(
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);
        float sx = (MathF.Abs(p.ScaleX) > 0.0001f ? p.ScaleX : 1f) * modelScale;
        float sy = (MathF.Abs(p.ScaleY) > 0.0001f ? p.ScaleY : 1f) * modelScale;
        float sz = (MathF.Abs(p.ScaleZ) > 0.0001f ? p.ScaleZ : 1f) * modelScale;
        return perm
            * Matrix4x4.CreateScale(sx, sz, sy)
            * Matrix4x4.CreateRotationY(-p.Angle)
            * Matrix4x4.CreateTranslation(p.X, posY, p.Y);
    }

    private string AppearanceId(W3Placement p)
    {
        string skin = p.SkinId;
        if (skin.Length == 4 && !skin.Contains('\0'))
            return skin;
        return p.Id;
    }

    // ------------------------------------------------------------------
    // Picking
    // ------------------------------------------------------------------

    /// <summary>Nearest placement whose bounding cylinder the ray hits, or null.</summary>
    public W3Placement? Pick(Vector3 origin, Vector3 dir)
    {
        W3Placement? best = null;
        float bestT = float.MaxValue;
        foreach ((W3Placement p, (Bank bank, Matrix4x4 _, Vector3 center)) in _instances)
        {
            if (p.IsUnit && !ShowUnits) continue;
            if (!p.IsUnit && !ShowDoodads) continue;
            float scale = MathF.Max(MathF.Abs(p.ScaleX), MathF.Max(MathF.Abs(p.ScaleY), 1f));
            float radius = MathF.Max(bank.RadiusW3 * scale, 32f);
            float minY = center.Y + bank.MinZW3 * MathF.Abs(p.ScaleZ <= 0.0001f ? 1f : p.ScaleZ);
            float maxY = center.Y + MathF.Max(bank.MaxZW3, 16f) * MathF.Abs(p.ScaleZ <= 0.0001f ? 1f : p.ScaleZ);
            // Ray vs vertical cylinder (XZ circle, Y slab).
            Vector2 oc = new(origin.X - center.X, origin.Z - center.Z);
            Vector2 d2 = new(dir.X, dir.Z);
            float a = Vector2.Dot(d2, d2);
            float t;
            if (a < 1e-6f)
            {
                if (oc.Length() > radius) continue;
                t = 0f;
            }
            else
            {
                float b = 2f * Vector2.Dot(oc, d2);
                float c = Vector2.Dot(oc, oc) - radius * radius;
                float disc = b * b - 4f * a * c;
                if (disc < 0f) continue;
                t = (-b - MathF.Sqrt(disc)) / (2f * a);
                if (t < 0f) t = 0f;
            }
            float hitY = origin.Y + dir.Y * t;
            if (hitY < minY - 16f || hitY > maxY + 16f)
            {
                // Try the slab planes (top hit for tall models seen from above).
                bool ok = false;
                foreach (float planeY in new[] { maxY, minY })
                {
                    if (MathF.Abs(dir.Y) < 1e-6f) break;
                    float tp = (planeY - origin.Y) / dir.Y;
                    if (tp < 0f) continue;
                    Vector3 hp = origin + dir * tp;
                    if (new Vector2(hp.X - center.X, hp.Z - center.Z).Length() <= radius)
                    {
                        t = tp;
                        ok = true;
                        break;
                    }
                }
                if (!ok) continue;
            }
            if (t < bestT)
            {
                bestT = t;
                best = p;
            }
        }
        return best;
    }

    /// <summary>World-space center of a placement (after ground snap), for screen projection.</summary>
    public Vector3? CenterOf(W3Placement p) =>
        _instances.TryGetValue(p, out var entry) ? entry.Center : null;

    /// <summary>Pick radius (world units) of a placement, for selection rings.</summary>
    public float RadiusOf(W3Placement p)
    {
        if (!_instances.TryGetValue(p, out var entry))
            return 64f;
        float scale = MathF.Max(MathF.Abs(p.ScaleX), 1f);
        return MathF.Max(entry.Bank.RadiusW3 * scale, 48f);
    }

    // ------------------------------------------------------------------
    // GL lifecycle
    // ------------------------------------------------------------------

    public void Initialize(GL gl)
    {
        _gl = gl;
        _banks.Clear();
        _failedBanks.Clear();
        _models.Clear();
        _textures.Clear();
        _instances.Clear();
        _program = 0;
        _whiteTexture = 0;
        _ghostInstanceVbo = 0;
        _dirty = true;
    }

    private void EnsureProgram(Func<string, string, uint> compile)
    {
        if (_program == 0)
            _program = compile(VertexSource, FragmentSource);
        if (_whiteTexture == 0)
        {
            GL gl = _gl!;
            _whiteTexture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, _whiteTexture);
            ReadOnlySpan<byte> white = stackalloc byte[4] { 255, 255, 255, 255 };
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, white);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }
        if (_ghostInstanceVbo == 0)
            _ghostInstanceVbo = _gl!.GenBuffer();
    }

    // ------------------------------------------------------------------
    // Bank building
    // ------------------------------------------------------------------

    private void RebuildInstances()
    {
        if (_gl == null || _catalog == null || _assets == null)
            return;
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        foreach (Bank bank in _banks.Values)
        {
            bank.Members.Clear();
            bank.InstanceCount = 0;
        }
        _instances.Clear();
        MissingModelCount = 0;

        foreach (W3Placement p in _doodads)
            AssignToBank(p);
        foreach (W3Placement p in _units)
            AssignToBank(p);

        // Upload per-bank instance buffers (mat4 rows + rgba tint = 20 floats).
        GL gl = _gl;
        RenderedCount = 0;
        foreach (Bank bank in _banks.Values)
        {
            int count = bank.Members.Count;
            bank.InstanceCount = count;
            if (count == 0)
                continue;
            RenderedCount += count;
            var data = new float[count * 20];
            for (int i = 0; i < count; i++)
            {
                W3Placement p = bank.Members[i];
                float modelScale = _catalog.ModelScale(AppearanceId(p), p.IsUnit);
                Matrix4x4 world = WorldMatrix(p, modelScale);
                WriteInstance(data.AsSpan(i * 20, 20), world, InstanceColor(p, bank.IsPlaceholder));
                _instances[p] = (bank, world, world.Translation);
            }
            if (bank.InstanceVbo == 0)
                bank.InstanceVbo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, bank.InstanceVbo);
            gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)data, BufferUsageARB.DynamicDraw);
            bank.InstanceCapacity = count;
        }
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        Console.Error.WriteLine(
            $"[DoodadPass] rebuild {totalSw.ElapsedMilliseconds}ms banks={_banks.Count} instances={RenderedCount} missing={MissingModelCount}");
    }

    /// <summary>Placeholder boxes are tinted (start-location gold, unit team color); real models keep albedo.</summary>
    private static Vector4 InstanceColor(W3Placement p, bool placeholder)
    {
        if (!placeholder)
            return Vector4.One;
        if (p.IsStartLocation)
            return new Vector4(1f, 0.85f, 0.2f, 1f);
        if (p.IsUnit)
            return new Vector4(PlayerColors[Math.Clamp(p.PlayerOwner, 0, PlayerColors.Length - 1)], 1f);
        return new Vector4(0.65f, 0.6f, 0.5f, 1f);
    }

    private static void WriteInstance(Span<float> dst, Matrix4x4 m, Vector4 color)
    {
        dst[0] = m.M11; dst[1] = m.M12; dst[2] = m.M13; dst[3] = m.M14;
        dst[4] = m.M21; dst[5] = m.M22; dst[6] = m.M23; dst[7] = m.M24;
        dst[8] = m.M31; dst[9] = m.M32; dst[10] = m.M33; dst[11] = m.M34;
        dst[12] = m.M41; dst[13] = m.M42; dst[14] = m.M43; dst[15] = m.M44;
        dst[16] = color.X; dst[17] = color.Y; dst[18] = color.Z; dst[19] = color.W;
    }

    private string ResolveBankKey(W3Placement p)
    {
        string appearance = AppearanceId(p);
        string rel = _catalog!.ResolveModelRel(appearance, p.IsUnit, p.Variation);
        if (p.IsStartLocation)
            return "__start__";
        if (rel.Length == 0 || _assets!.ResolveModel(rel) == null)
            return p.IsUnit ? "__unit__" : "__doodad__";
        string tex = p.IsUnit ? "" : _catalog.DestructibleTexFile(appearance);
        return rel + "|" + tex;
    }

    private void AssignToBank(W3Placement p)
    {
        if (p.IsUnit && !ShowUnits) return;
        if (!p.IsUnit && !ShowDoodads) return;
        string key = ResolveBankKey(p);
        if (key.StartsWith("__", StringComparison.Ordinal))
            MissingModelCount += p.IsStartLocation ? 0 : 1;
        Bank? bank = GetOrBuildBank(key);
        if (bank == null)
            return;
        bank.Members.Add(p);
    }

    private readonly HashSet<string> _failedBanks = new(StringComparer.OrdinalIgnoreCase);

    private Bank? GetOrBuildBank(string key)
    {
        if (_banks.TryGetValue(key, out Bank? existing))
            return existing;
        if (_failedBanks.Contains(key))
            return null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Bank? bank = key switch
        {
            "__start__" => BuildPlaceholderBank(radius: 64f, height: 24f),
            "__unit__" => BuildPlaceholderBank(radius: 28f, height: 96f),
            "__doodad__" => BuildPlaceholderBank(radius: 36f, height: 48f),
            _ => BuildModelBank(key),
        };
        if (sw.ElapsedMilliseconds > 150)
            Console.Error.WriteLine($"[DoodadPass] bank {sw.ElapsedMilliseconds}ms {key}");
        if (bank == null)
        {
            _failedBanks.Add(key);
            return null;
        }
        _banks[key] = bank;
        return bank;
    }

    private Bank? BuildModelBank(string key)
    {
        int split = key.LastIndexOf('|');
        string rel = key[..split];
        string replaceableTex = key[(split + 1)..];
        string? absolute = _assets!.ResolveModel(rel);
        if (absolute == null)
            return null;
        if (!_models.TryGetValue(absolute, out MdxStaticModel? model))
        {
            model = MdxStaticModel.LoadFile(absolute);
            _models[absolute] = model;
        }
        if (model == null)
            return null;

        var bank = new Bank
        {
            RadiusW3 = model.BoundsRadius,
            MinZW3 = model.BoundsMinZ,
            MaxZW3 = model.BoundsMaxZ,
        };

        // Reforged LOD banks: keep the base LOD only (w3_mdx_batcher base_geosets).
        int minLod = int.MaxValue;
        foreach (MdxStaticModel.Geoset g in model.Geosets)
            minLod = Math.Min(minLod, g.Lod);

        // Merge geosets by (texture, mode) into one surface each.
        var groups = new Dictionary<(uint Texture, int Mode), (List<float> V, List<uint> I)>();
        foreach (MdxStaticModel.Geoset geoset in model.Geosets)
        {
            if (geoset.Lod != minLod)
                continue;
            if (!model.GeosetVisibleAtStand(geoset))
                continue;
            string texRel = model.ResolveTexturePath(geoset, replaceableTex);
            uint texture = GetTexture(texRel);
            MdxStaticModel.MaterialLayer? layer = model.MaterialFor(geoset);
            int mode = (layer?.FilterMode ?? 0) switch
            {
                0 => SurfaceOpaque,
                1 => SurfaceCutout,
                2 => SurfaceBlend,
                _ => SurfaceAdditive,
            };
            if (mode == SurfaceOpaque && LooksCutout(texRel))
                mode = SurfaceCutout;
            if (!groups.TryGetValue((texture, mode), out var group))
            {
                group = (new List<float>(), new List<uint>());
                groups[(texture, mode)] = group;
            }
            AppendGeoset(group.V, group.I, geoset);
        }
        foreach (((uint texture, int mode), (List<float> vertices, List<uint> indices)) in groups)
        {
            if (indices.Count == 0)
                continue;
            bank.Surfaces.Add(UploadSurface(vertices, indices, texture, mode));
        }
        return bank.Surfaces.Count > 0 ? bank : null;
    }

    private static bool LooksCutout(string texRel)
    {
        string t = texRel.ToLowerInvariant();
        return t.Contains("tree") || t.Contains("leaf") || t.Contains("canopy") || t.Contains("plant");
    }

    /// <summary>W3-space vertices go into the VBO unchanged; the permutation lives in the instance matrix.</summary>
    private static void AppendGeoset(List<float> vertices, List<uint> indices, MdxStaticModel.Geoset geoset)
    {
        uint indexBase = (uint)(vertices.Count / 8);
        int vertexCount = geoset.Positions.Count / 3;
        for (int i = 0; i < vertexCount; i++)
        {
            vertices.Add(geoset.Positions[i * 3]);
            vertices.Add(geoset.Positions[i * 3 + 1]);
            vertices.Add(geoset.Positions[i * 3 + 2]);
            vertices.Add(geoset.Normals[i * 3]);
            vertices.Add(geoset.Normals[i * 3 + 1]);
            vertices.Add(geoset.Normals[i * 3 + 2]);
            vertices.Add(geoset.Uvs[i * 2]);
            vertices.Add(geoset.Uvs[i * 2 + 1]);
        }
        foreach (ushort face in geoset.Faces)
            indices.Add(indexBase + face);
    }

    private Bank BuildPlaceholderBank(float radius, float height)
    {
        // Axis-aligned box in W3 space: XY = ±radius, Z = 0..height.
        var v = new List<float>();
        var idx = new List<uint>();
        Vector3[] corners =
        {
            new(-radius, -radius, 0), new(radius, -radius, 0), new(radius, radius, 0), new(-radius, radius, 0),
            new(-radius, -radius, height), new(radius, -radius, height), new(radius, radius, height), new(-radius, radius, height),
        };
        int[][] faces =
        {
            new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 },
            new[] { 3, 0, 4, 7 }, new[] { 4, 5, 6, 7 }, new[] { 3, 2, 1, 0 },
        };
        Vector3[] normals =
        {
            new(0, -1, 0), new(1, 0, 0), new(0, 1, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1),
        };
        for (int f = 0; f < faces.Length; f++)
        {
            uint baseIndex = (uint)(v.Count / 8);
            foreach (int c in faces[f])
            {
                v.Add(corners[c].X); v.Add(corners[c].Y); v.Add(corners[c].Z);
                v.Add(normals[f].X); v.Add(normals[f].Y); v.Add(normals[f].Z);
                v.Add(0.5f); v.Add(0.5f);
            }
            idx.Add(baseIndex); idx.Add(baseIndex + 1); idx.Add(baseIndex + 2);
            idx.Add(baseIndex); idx.Add(baseIndex + 2); idx.Add(baseIndex + 3);
        }
        var bank = new Bank { RadiusW3 = radius * 1.4f, MinZW3 = 0, MaxZW3 = height, IsPlaceholder = true };
        bank.Surfaces.Add(UploadSurface(v, idx, _whiteTexture, SurfaceOpaque));
        return bank;
    }

    private Surface UploadSurface(List<float> vertices, List<uint> indices, uint texture, int mode)
    {
        GL gl = _gl!;
        var surface = new Surface
        {
            Vbo = gl.GenBuffer(),
            Ebo = gl.GenBuffer(),
            IndexCount = indices.Count,
            Texture = texture,
            Mode = mode,
        };
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, surface.Vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)vertices.ToArray(), BufferUsageARB.StaticDraw);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, surface.Ebo);
        gl.BufferData(BufferTargetARB.ElementArrayBuffer, (ReadOnlySpan<uint>)indices.ToArray(), BufferUsageARB.StaticDraw);
        surface.Vao = 0; // created lazily against the bank instance VBO
        surface.GhostVao = 0;
        return surface;
    }

    private unsafe uint BuildVao(Surface surface, uint instanceVbo)
    {
        GL gl = _gl!;
        uint vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, surface.Vbo);
        const uint stride = 8 * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, null);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, instanceVbo);
        const uint instStride = 20 * sizeof(float);
        for (uint slot = 0; slot < 5; slot++)
        {
            gl.EnableVertexAttribArray(3 + slot);
            gl.VertexAttribPointer(3 + slot, 4, VertexAttribPointerType.Float, false, instStride, (void*)(slot * 4 * sizeof(float)));
            gl.VertexAttribDivisor(3 + slot, 1);
        }
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, surface.Ebo);
        gl.BindVertexArray(0);
        return vao;
    }

    private uint GetTexture(string classicRel)
    {
        if (classicRel.Length == 0)
            return _whiteTexture;
        if (_textures.TryGetValue(classicRel, out uint cached))
            return cached;
        GL gl = _gl!;
        RgbaImage? image = _assets!.LoadTexture(classicRel);
        uint texture;
        if (image == null)
        {
            texture = _whiteTexture;
        }
        else
        {
            texture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, (ReadOnlySpan<byte>)image.Pixels);
            gl.GenerateMipmap(TextureTarget.Texture2D);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }
        _textures[classicRel] = texture;
        return texture;
    }

    // ------------------------------------------------------------------
    // Draw
    // ------------------------------------------------------------------

    public void Render(Matrix4x4 vp, Vector3 lightDir, float ambient, float lightScale, Func<string, string, uint> compile)
    {
        if (_gl == null || (_doodads.Count == 0 && _units.Count == 0 && Ghost == null))
            return;
        GL gl = _gl;
        EnsureProgram(compile);
        if (_dirty)
        {
            RebuildInstances();
            _dirty = false;
        }

        gl.UseProgram(_program);
        SetMatrix(gl, "u_vp", vp);
        SetVec3(gl, "u_light_dir", lightDir);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_ambient"), ambient);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_light_scale"), lightScale);
        gl.Uniform4(gl.GetUniformLocation(_program, "u_tint"), 1f, 1f, 1f, 1f);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_texture"), 0);
        gl.ActiveTexture(TextureUnit.Texture0);

        // Pass 1: opaque + cutout (depth write on).
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(true);
        DrawSurfaces(gl, mode => mode is SurfaceOpaque or SurfaceCutout);

        // Pass 2: blend/additive (no depth write).
        gl.Enable(EnableCap.Blend);
        gl.DepthMask(false);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        DrawSurfaces(gl, mode => mode == SurfaceBlend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        DrawSurfaces(gl, mode => mode == SurfaceAdditive);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(true);

        DrawHighlights(gl);
        DrawGhost(gl);
        gl.Disable(EnableCap.Blend);
        gl.BindVertexArray(0);
    }

    private unsafe void DrawSurfaces(GL gl, Func<int, bool> modeFilter)
    {
        foreach (Bank bank in _banks.Values)
        {
            if (bank.InstanceCount == 0)
                continue;
            foreach (Surface surface in bank.Surfaces)
            {
                if (!modeFilter(surface.Mode))
                    continue;
                if (surface.Vao == 0)
                    surface.Vao = BuildVao(surface, bank.InstanceVbo);
                gl.Uniform1(gl.GetUniformLocation(_program, "u_mode"), surface.Mode);
                gl.BindTexture(TextureTarget.Texture2D, surface.Texture);
                gl.BindVertexArray(surface.Vao);
                gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)surface.IndexCount,
                    DrawElementsType.UnsignedInt, null, (uint)bank.InstanceCount);
            }
        }
    }

    private void DrawHighlights(GL gl)
    {
        if (Selection.Count == 0)
            return;
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(false);
        gl.Uniform4(gl.GetUniformLocation(_program, "u_tint"), 1f, 0.82f, 0.25f, 0.4f);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_mode"), 9); // flat tint mode
        foreach (W3Placement p in Selection)
        {
            if (!_instances.TryGetValue(p, out var entry))
                continue;
            DrawSingle(gl, entry.Bank, entry.World, new Vector4(1f, 0.82f, 0.25f, 1f));
        }
        gl.Uniform4(gl.GetUniformLocation(_program, "u_tint"), 1f, 1f, 1f, 1f);
        gl.DepthMask(true);
    }

    private void DrawGhost(GL gl)
    {
        W3Placement? ghost = Ghost;
        if (ghost == null || _catalog == null)
            return;
        string key = ResolveBankKey(ghost);
        Bank? bank = GetOrBuildBank(key);
        if (bank == null)
            return;
        Matrix4x4 world = WorldMatrix(ghost, _catalog.ModelScale(AppearanceId(ghost), ghost.IsUnit));
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(false);
        gl.Uniform4(gl.GetUniformLocation(_program, "u_tint"), 1f, 1f, 1f, 0.55f);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_mode"), 8); // ghost: textured translucent
        DrawSingle(gl, bank, world, Vector4.One);
        gl.Uniform4(gl.GetUniformLocation(_program, "u_tint"), 1f, 1f, 1f, 1f);
        gl.DepthMask(true);
    }

    private unsafe void DrawSingle(GL gl, Bank bank, Matrix4x4 world, Vector4 color)
    {
        Span<float> data = stackalloc float[20];
        WriteInstance(data, world, color);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _ghostInstanceVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)data, BufferUsageARB.StreamDraw);
        foreach (Surface surface in bank.Surfaces)
        {
            if (surface.GhostVao == 0)
                surface.GhostVao = BuildVao(surface, _ghostInstanceVbo);
            gl.BindTexture(TextureTarget.Texture2D, surface.Texture);
            gl.BindVertexArray(surface.GhostVao);
            gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)surface.IndexCount,
                DrawElementsType.UnsignedInt, null, 1);
        }
    }

    // ------------------------------------------------------------------
    // Palette thumbnails (offscreen model renders)
    // ------------------------------------------------------------------

    private uint _thumbFbo, _thumbColor, _thumbDepth;
    private int _thumbSize;

    /// <summary>
    /// Renders a model prototype into a small offscreen FBO for palette
    /// thumbnails. Returns bottom-up RGBA pixels (transparent background),
    /// or null when the model is missing/placeholder. GL thread only.
    /// </summary>
    public unsafe byte[]? RenderThumbnail(W3Placement proto, int size, Func<string, string, uint> compile)
    {
        if (_gl == null || _catalog == null)
            return null;
        GL gl = _gl;
        string key = ResolveBankKey(proto);
        if (key.StartsWith("__", StringComparison.Ordinal))
            return null;
        Bank? bank = GetOrBuildBank(key);
        if (bank == null || bank.Surfaces.Count == 0)
            return null;
        EnsureProgram(compile);

        if (_thumbFbo == 0 || _thumbSize != size)
        {
            if (_thumbFbo != 0)
            {
                gl.DeleteFramebuffer(_thumbFbo);
                gl.DeleteRenderbuffer(_thumbColor);
                gl.DeleteRenderbuffer(_thumbDepth);
            }
            _thumbFbo = gl.GenFramebuffer();
            _thumbColor = gl.GenRenderbuffer();
            _thumbDepth = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _thumbColor);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)size, (uint)size);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _thumbDepth);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)size, (uint)size);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, _thumbFbo);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                RenderbufferTarget.Renderbuffer, _thumbColor);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, _thumbDepth);
            if ((GLEnum)gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            {
                // Would otherwise silently read back a black image.
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                gl.DeleteFramebuffer(_thumbFbo);
                gl.DeleteRenderbuffer(_thumbColor);
                gl.DeleteRenderbuffer(_thumbDepth);
                _thumbFbo = 0;
                return null;
            }
            _thumbSize = size;
        }
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _thumbFbo);
        gl.Viewport(0, 0, (uint)size, (uint)size);
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        // Fit camera to the model bounds (W3 Z = editor Y after the perm swap).
        float ms = _catalog.ModelScale(AppearanceId(proto), proto.IsUnit);
        float radius = MathF.Max(bank.RadiusW3 * ms, 16f);
        float minY = bank.MinZW3 * ms;
        float maxY = MathF.Max(bank.MaxZW3 * ms, 24f);
        var target = new Vector3(0f, (minY + maxY) * 0.5f, 0f);
        float fit = MathF.Max(radius, (maxY - minY) * 0.6f);
        float dist = fit * 2.7f;
        const float yaw = 2.6f, pitch = 0.5f;
        var back = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        Matrix4x4 vp = Matrix4x4.CreateLookAt(target + back * dist, target, Vector3.UnitY)
            * Matrix4x4.CreatePerspectiveFieldOfView(40f * MathF.PI / 180f, 1f, dist * 0.05f, dist * 8f);

        var perm = new Matrix4x4(
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);
        Matrix4x4 world = perm * Matrix4x4.CreateScale(ms);

        gl.UseProgram(_program);
        SetMatrix(gl, "u_vp", vp);
        SetVec3(gl, "u_light_dir", Vector3.Normalize(new Vector3(0.45f, 0.85f, 0.28f)));
        gl.Uniform1(gl.GetUniformLocation(_program, "u_ambient"), 0.5f);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_light_scale"), 1.1f);
        gl.Uniform4(gl.GetUniformLocation(_program, "u_tint"), 1f, 1f, 1f, 1f);
        gl.Uniform1(gl.GetUniformLocation(_program, "u_texture"), 0);
        gl.ActiveTexture(TextureUnit.Texture0);

        Span<float> instance = stackalloc float[20];
        WriteInstance(instance, world, Vector4.One);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _ghostInstanceVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)instance, BufferUsageARB.StreamDraw);

        void DrawPass(Func<int, bool> filter)
        {
            foreach (Surface surface in bank.Surfaces)
            {
                if (!filter(surface.Mode))
                    continue;
                if (surface.GhostVao == 0)
                    surface.GhostVao = BuildVao(surface, _ghostInstanceVbo);
                gl.Uniform1(gl.GetUniformLocation(_program, "u_mode"), surface.Mode);
                gl.BindTexture(TextureTarget.Texture2D, surface.Texture);
                gl.BindVertexArray(surface.GhostVao);
                gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)surface.IndexCount,
                    DrawElementsType.UnsignedInt, null, 1);
            }
        }
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(true);
        DrawPass(mode => mode is SurfaceOpaque or SurfaceCutout);
        gl.Enable(EnableCap.Blend);
        gl.DepthMask(false);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        DrawPass(mode => mode == SurfaceBlend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        DrawPass(mode => mode == SurfaceAdditive);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
        gl.BindVertexArray(0);

        var pixels = new byte[size * size * 4];
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _thumbFbo);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* p = pixels)
        {
            gl.ReadPixels(0, 0, (uint)size, (uint)size, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return pixels;
    }

    private void SetMatrix(GL gl, string name, Matrix4x4 matrix)
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

    private void SetVec3(GL gl, string name, Vector3 value) =>
        _gl!.Uniform3(_gl.GetUniformLocation(_program, name), value.X, value.Y, value.Z);

    // ------------------------------------------------------------------
    // Shaders
    // ------------------------------------------------------------------

    private const string VertexSource = @"
layout(location = 0) in vec3 a_position; // W3 axes
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_uv;
layout(location = 3) in vec4 a_i0;
layout(location = 4) in vec4 a_i1;
layout(location = 5) in vec4 a_i2;
layout(location = 6) in vec4 a_i3;
layout(location = 7) in vec4 a_color;
uniform mat4 u_vp;
out vec3 v_normal;
out vec2 v_uv;
out vec4 v_color;

void main() {
    // Instance rows are System.Numerics rows -> GLSL columns == transpose,
    // so M * v here equals v(row) * M(numerics). Same trick as u_vp.
    mat4 im = mat4(a_i0, a_i1, a_i2, a_i3);
    vec4 world = im * vec4(a_position, 1.0);
    v_normal = normalize(mat3(im) * a_normal);
    v_uv = a_uv;
    v_color = a_color;
    gl_Position = u_vp * world;
}
";

    private const string FragmentSource = @"
in vec3 v_normal;
in vec2 v_uv;
in vec4 v_color;
uniform sampler2D u_texture;
uniform vec3 u_light_dir;
uniform float u_ambient;
uniform float u_light_scale;
uniform vec4 u_tint;
uniform int u_mode; // 0 opaque, 1 cutout, 2 blend, 3 additive, 8 ghost, 9 flat highlight
out vec4 o_color;

void main() {
    vec4 col = texture(u_texture, v_uv) * v_color;
    if ((u_mode == 1 || u_mode == 8) && col.a < 0.28) discard;
    float ndotl = max(dot(normalize(v_normal), normalize(u_light_dir)), 0.0);
    float light = min(1.0, mix(u_ambient, 1.0, ndotl) * u_light_scale);
    vec3 rgb = col.rgb * light;
    float alpha = (u_mode == 0 || u_mode == 1) ? 1.0 : col.a;
    if (u_mode == 9) {
        rgb = u_tint.rgb;
        alpha = u_tint.a;
    } else {
        alpha *= u_tint.a;
        rgb *= u_tint.rgb;
    }
    o_color = vec4(rgb, alpha);
}
";
}
