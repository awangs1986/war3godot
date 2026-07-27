using System.Numerics;
using System.Runtime.InteropServices;
using Diablo1Like.W3.Terrain;
using Silk.NET.OpenGL;
using W3Editor.Formats;
using W3Editor.PluginContracts;

namespace W3Editor.Rendering;

/// <summary>
/// Built-in OpenGL renderer for the editor 3D viewport (Silk.NET on top of
/// Avalonia's GL context). Consumes the shared W3TerrainCore algorithm layer —
/// GroundLayout / WaterLayout / WarsmashTerrainRules — so the block-map result
/// is byte-identical to the Godot runtime; only the lighting model is the
/// simple editor Lambert (w3_ground_unified ground_lighting).
/// </summary>
public sealed class GlTerrainScene : IDisposable
{
    private const int ArrayBankSize = 256;
    private const int ArrayBankCount = 3;

    private GL? _gl;
    private bool _es;

    private uint _groundProgram, _waterProgram, _cliffProgram, _lineProgram;
    private uint _quadVbo, _groundVao, _waterVao;
    private uint _heightTex, _normalHeightTex, _existsTex, _tileTex;
    private readonly uint[] _groundBanks = new uint[ArrayBankCount];
    private uint _waterHeightTex, _waterGroundTex, _waterExistsTex, _waterArrayTex;
    private uint _ringVao, _ringVbo;

    private sealed class CliffBatchGpu
    {
        public uint Vao, Vbo, Ebo, Texture;
        public int IndexCount;
    }

    private readonly List<CliffBatchGpu> _cliffGpu = new();
    private readonly Dictionary<int, uint> _cliffTextureCache = new();
    private readonly Dictionary<string, MdxStaticModel?> _modelCache = new(StringComparer.OrdinalIgnoreCase);

    // CPU state (valid off the GL thread for picking).
    private W3EMap? _source;
    private W3TerrainMap? _map;
    private WarsmashTerrainRules? _rules;
    private GroundLayout? _layout;
    private WaterLayout? _waterLayout;
    private EditorAssets? _assets;
    private WaterConfig _waterConfig = new();
    private int _waterFrames;
    private float[] _heights = Array.Empty<float>();
    private readonly List<bool> _extended = new();
    private List<int> _cliffToGround = new();
    private readonly object _sync = new();

    private bool _rebuildAll;      // tileset/map identity changed → rebuild everything
    private bool _rebuildDynamic;  // corner data changed → re-upload height/exists/tile/water + cliffs

    public EditorCamera Camera { get; } = new();
    public Vector3 LightDirection = Vector3.Normalize(new Vector3(0.45f, 0.85f, 0.28f));
    public float LightAmbient = 0.35f;
    public float LightScale = 1.1f;
    /// <summary>Brush ring overlay: map-corner center + radius in corners; null hides it.</summary>
    public (float CornerX, float CornerY, float Radius)? BrushRing;
    /// <summary>Tile grid overlay (HiveWE tile_grid: white 1px lines at tile boundaries).</summary>
    public bool ShowGrid = true;
    public bool ShowWater = true;
    public bool ShowCliffs = true;
    public bool ShowBrush = true;
    public bool ShowClickHelpers = true;
    public bool ShowRegions;
    public IReadOnlyList<W3RegionRecord> Regions { get; set; } = Array.Empty<W3RegionRecord>();
    public bool LightingEnabled = true;
    /// <summary>Doodad/unit render pass (instanced static MDX + placeholders).</summary>
    public GlDoodadPass Placements { get; } = new();
    /// <summary>Single-slot plugin FBX/mesh preview (not written to placements).</summary>
    public PluginPreviewPass PluginPreview { get; } = new();

    public bool HasMap => _map != null;
    public bool HasPluginPreview => PluginPreview.HasPreview;
    public string StatusLine { get; private set; } = "";

    // ------------------------------------------------------------------
    // CPU side
    // ------------------------------------------------------------------

    /// <summary>Adopts a map (called on the UI thread; GPU rebuild happens next frame).</summary>
    public void SetMap(W3EMap source, EditorAssets assets)
    {
        lock (_sync)
        {
            _source = source;
            _assets = assets;
            _map = TerrainMirror.Create(source);
            RebuildCpu();
            Camera.Frame(
                _map.CenterOffsetX + _map.TileWidth * W3TerrainMap.TileSize * 0.5f,
                _map.CenterOffsetY + _map.TileHeight * W3TerrainMap.TileSize * 0.5f,
                Math.Max(_map.TileWidth, _map.TileHeight) * W3TerrainMap.TileSize * 0.7f);
            _rebuildAll = true;
        }
    }

    public void ClearMap()
    {
        lock (_sync)
        {
            _source = null;
            _map = null;
            _rules = null;
            _layout = null;
            _waterLayout = null;
            _heights = Array.Empty<float>();
            Placements.Clear();
            PluginPreview.Clear();
            _rebuildAll = true;
        }
    }

    /// <summary>
    /// Replaces the single plugin preview slot. Origin = map-center ground height.
    /// Safe without a GL context (GPU upload happens on next <see cref="Render"/>).
    /// </summary>
    public void SetPluginPreview(PluginMeshPreview mesh, string label)
    {
        lock (_sync)
        {
            float cx = 0f, cy = 0f, cz = 0f;
            if (_map != null)
            {
                cx = _map.CenterOffsetX + _map.TileWidth * W3TerrainMap.TileSize * 0.5f;
                cz = _map.CenterOffsetY + _map.TileHeight * W3TerrainMap.TileSize * 0.5f;
                cy = SampleHeightWorld(cx, cz);
            }
            PluginPreview.Set(mesh, label, new Vector3(cx, cy, cz));
        }
    }

    /// <summary>Clears the plugin preview slot (map close / switch / explicit).</summary>
    public void ClearPluginPreview()
    {
        lock (_sync)
        {
            PluginPreview.Clear();
        }
    }

    /// <summary>Hands placement lists to the doodad pass (call after SetMap and on doo edits).</summary>
    public void SetPlacements(
        IReadOnlyList<W3Placement> doodads, IReadOnlyList<W3Placement> units,
        Services.IObjectCatalog catalog, EditorAssets assets)
    {
        lock (_sync)
        {
            Placements.SetData(doodads, units, catalog, assets, SampleHeightWorld);
        }
    }

    /// <summary>Placement content changed (place/move/delete/undo).</summary>
    public void NotifyPlacementsChanged()
    {
        lock (_sync)
        {
            Placements.MarkDirty();
        }
    }

    /// <summary>Ground height (world Y) at editor world XZ.</summary>
    public float SampleHeightWorld(float worldX, float worldZ)
    {
        if (_map == null)
            return 0f;
        return SampleHeight(
            (worldX - _map.CenterOffsetX) / W3TerrainMap.TileSize,
            (worldZ - _map.CenterOffsetY) / W3TerrainMap.TileSize);
    }

    /// <summary>Picks a placement under the pixel, or null (bounding-cylinder test).</summary>
    public W3Placement? PickPlacement(float px, float py, float width, float height)
    {
        lock (_sync)
        {
            if (_map == null)
                return null;
            (Vector3 origin, Vector3 dir) = Camera.PixelRay(px, py, width, height);
            return Placements.Pick(origin, dir);
        }
    }

    /// <summary>Projects a world point to pixel coordinates (null when behind the camera).</summary>
    public Vector2? ProjectWorld(Vector3 world, float width, float height)
    {
        Matrix4x4 vp = Camera.ViewProjection(width / Math.Max(height, 1f));
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), vp);
        if (clip.W <= 0.0001f)
            return null;
        return new Vector2(
            (clip.X / clip.W * 0.5f + 0.5f) * width,
            (0.5f - clip.Y / clip.W * 0.5f) * height);
    }

    /// <summary>World center of a placement as currently rendered (ground-snapped).</summary>
    public Vector3? PlacementCenter(W3Placement p)
    {
        lock (_sync)
        {
            return Placements.CenterOf(p);
        }
    }

    /// <summary>Corner data changed (brush stroke, undo). Region in corner coords.</summary>
    public void NotifyCornersChanged(int x0, int y0, int x1, int y1)
    {
        lock (_sync)
        {
            if (_source == null || _map == null)
                return;
            TerrainMirror.SyncRegion(_source, _map, x0 - 4, y0 - 4, x1 + 4, y1 + 4);
            RebuildCpu();
            Placements.MarkDirty(); // ground-snap heights may have changed
            _rebuildDynamic = true;
        }
    }

    /// <summary>Tileset list / whole map content changed — full GPU rebuild.</summary>
    public void NotifyMapReplaced()
    {
        lock (_sync)
        {
            if (_source == null)
                return;
            _map = TerrainMirror.Create(_source);
            RebuildCpu();
            Placements.MarkDirty();
            _rebuildAll = true;
        }
    }

    private void RebuildCpu()
    {
        if (_map == null || _assets == null)
            return;
        _cliffToGround = _assets.BuildCliffToGround(_map);
        _rules = new WarsmashTerrainRules(
            _map,
            rel => _assets.ResolveModel(rel) != null,
            texture => _assets.CliffTypeFor(_map, texture).CliffModelDir,
            texture => _assets.CliffTypeFor(_map, texture).RampModelDir,
            (tag, authored) => Math.Clamp(authored, 0, WarsmashTerrainRules.CliffVariationMaximum.GetValueOrDefault(tag, 0)));
        _cliffPlacements = _rules.RebuildCliffs(); // also refreshes Cliff/Romp corner flags
        // _extended is only known after atlases decode; keep previous values and
        // let the full rebuild path refresh it.
        int layerCount = _map.GroundTilesets.Count + 1;
        while (_extended.Count < layerCount)
            _extended.Add(false);
        _layout = new GroundLayout(_map, _rules, _extended, _cliffToGround, layerCount);
        _waterLayout = new WaterLayout(_map);
        _heights = _layout.BuildHeightMap();
    }

    private IReadOnlyList<WarsmashTerrainRules.CliffPlacement> _cliffPlacements =
        Array.Empty<WarsmashTerrainRules.CliffPlacement>();

    /// <summary>Bilinear ground height (world Y) at map-corner coordinates.</summary>
    public float SampleHeight(float cornerX, float cornerY)
    {
        if (_map == null || _heights.Length == 0)
            return 0f;
        float fx = Math.Clamp(cornerX, 0f, _map.Width - 1.001f);
        float fy = Math.Clamp(cornerY, 0f, _map.Height - 1.001f);
        int x0 = (int)fx, y0 = (int)fy;
        float tx = fx - x0, ty = fy - y0;
        float h00 = _heights[y0 * _map.Width + x0];
        float h10 = _heights[y0 * _map.Width + Math.Min(x0 + 1, _map.Width - 1)];
        float h01 = _heights[Math.Min(y0 + 1, _map.Height - 1) * _map.Width + x0];
        float h11 = _heights[Math.Min(y0 + 1, _map.Height - 1) * _map.Width + Math.Min(x0 + 1, _map.Width - 1)];
        return (h00 + (h10 - h00) * tx) * (1 - ty) + (h01 + (h11 - h01) * tx) * ty;
    }

    /// <summary>
    /// Ray-marches the heightfield (same algorithm as w3_height_sampler.gd
    /// pick_ground) and returns fractional corner coordinates, or null.
    /// </summary>
    public (float CornerX, float CornerY)? Pick(float px, float py, float width, float height)
    {
        if (_map == null)
            return null;
        (Vector3 origin, Vector3 dir) = Camera.PixelRay(px, py, width, height);
        const float step = 32f;
        const float maxDistance = 120000f;
        float previousDelta = HeightDelta(origin);
        for (float t = step; t < maxDistance; t += step)
        {
            Vector3 point = origin + dir * t;
            float delta = HeightDelta(point);
            if (previousDelta > 0f && delta <= 0f)
            {
                // Bisect between t-step and t.
                float lo = t - step, hi = t;
                for (int i = 0; i < 24; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (HeightDelta(origin + dir * mid) > 0f) lo = mid;
                    else hi = mid;
                }
                Vector3 hit = origin + dir * ((lo + hi) * 0.5f);
                float cx = (hit.X - _map.CenterOffsetX) / W3TerrainMap.TileSize;
                float cy = (hit.Z - _map.CenterOffsetY) / W3TerrainMap.TileSize;
                if (cx < -0.5f || cy < -0.5f || cx > _map.Width - 0.5f || cy > _map.Height - 0.5f)
                    return null;
                return (cx, cy);
            }
            previousDelta = delta;
        }
        return null;
    }

    /// <summary>
    /// Keeps the eye above the terrain: if the eye would sink below the ground
    /// under it (plus a margin), pitch is raised just enough to stay above.
    /// Iterated a few times because raising the pitch also moves the eye XZ.
    /// </summary>
    private void ClampCameraAboveGround()
    {
        if (_map == null || _heights.Length == 0)
            return;
        const float margin = 48f;
        const float maxPitch = MathF.PI / 2f - 0.001f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 eye = Camera.Position;
            float cx = (eye.X - _map.CenterOffsetX) / W3TerrainMap.TileSize;
            float cy = (eye.Z - _map.CenterOffsetY) / W3TerrainMap.TileSize;
            float minEyeY = SampleHeight(cx, cy) + margin;
            if (eye.Y >= minEyeY)
                break;
            float sinPitch = Math.Clamp((minEyeY - Camera.Target.Y) / Camera.Distance, -1f, 1f);
            float required = MathF.Asin(sinPitch);
            if (required <= Camera.Pitch)
                break; // can't fix by pitching further up (e.g. target under a cliff)
            Camera.Pitch = MathF.Min(required, maxPitch);
        }
    }

    private float HeightDelta(Vector3 world)
    {
        float cx = (world.X - _map!.CenterOffsetX) / W3TerrainMap.TileSize;
        float cy = (world.Z - _map.CenterOffsetY) / W3TerrainMap.TileSize;
        return world.Y - SampleHeight(cx, cy);
    }

    // ------------------------------------------------------------------
    // GL lifecycle
    // ------------------------------------------------------------------

    public void Initialize(Func<string, nint> getProcAddress)
    {
        _gl = GL.GetApi(name => getProcAddress(name));
        string version = _gl.GetStringS(StringName.Version) ?? "";
        _es = version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
        BuildPrograms();
        BuildQuad();
        Placements.Initialize(_gl);
        PluginPreview.Initialize(_gl);
        _ringVao = _gl.GenVertexArray();
        _ringVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_ringVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _ringVbo);
        _gl.EnableVertexAttribArray(0);
        unsafe { _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, null); }
        _gl.BindVertexArray(0);
    }

    public void Render(int fb, int width, int height, double timeSeconds)
    {
        if (_gl == null)
            return;
        GL gl = _gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.ClearColor(0.10f, 0.11f, 0.13f, 1f);
        gl.ClearDepth(1f);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        lock (_sync)
        {
            if (_rebuildAll)
            {
                UploadStaticResources();
                UploadDynamicResources();
                _rebuildAll = false;
                _rebuildDynamic = false;
            }
            else if (_rebuildDynamic)
            {
                UploadDynamicResources();
                _rebuildDynamic = false;
            }
            if (_map == null)
                return;

            ClampCameraAboveGround();
            Matrix4x4 vp = Camera.ViewProjection(width / (float)Math.Max(height, 1));
            DrawGround(vp);
            if (ShowCliffs) DrawCliffs(vp);
            float ambient = LightingEnabled ? LightAmbient : 1f;
            float scale = LightingEnabled ? LightScale : 0f;
            Placements.Render(vp, LightDirection, ambient, scale, Compile);
            PluginPreview.Render(vp, LightDirection, ambient, scale, Compile);
            if (ShowWater) DrawWater(vp, timeSeconds);
            if (ShowBrush) DrawBrushRing(vp);
            if (ShowRegions) DrawRegionRings(vp);
            if (ShowClickHelpers) DrawSelectionRings(vp);
        }
    }

    /// <summary>
    /// Offscreen capture: renders the scene into a private FBO at the requested
    /// size and encodes a PNG. Independent of window visibility/focus/DPI
    /// (replaces PrintWindow captures). Must run on the GL thread.
    /// Returns null on success, otherwise an error message.
    /// </summary>
    public unsafe string? CaptureToPng(string path, int width, int height, double timeSeconds)
    {
        if (_gl == null)
            return "GL not initialized";
        if (_map == null)
            return "no map loaded";
        // Guard int overflow in width*height*4 and renderbuffer limits.
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            return $"capture size out of range: {width}x{height} (1..8192)";
        GL gl = _gl;

        uint fbo = gl.GenFramebuffer();
        uint color = gl.GenRenderbuffer();
        uint depth = gl.GenRenderbuffer();
        try
        {
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, color);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)width, (uint)height);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                RenderbufferTarget.Renderbuffer, color);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, depth);
            var status = (GLEnum)gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
                return $"offscreen FBO incomplete: {status}";

            Render((int)fbo, width, height, timeSeconds);

            var pixels = new byte[width * height * 4];
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
            gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
            fixed (byte* p = pixels)
            {
                gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            }

            // GL rows are bottom-up; flip and force opaque alpha while copying.
            using var bmp = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(
                width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque));
            IntPtr dst = bmp.GetPixels();
            int stride = width * 4;
            for (int row = 0; row < height; row++)
            {
                int src = (height - 1 - row) * stride;
                for (int i = src + 3; i < src + stride; i += 4)
                    pixels[i] = 255;
                Marshal.Copy(pixels, src, dst + row * stride, stride);
            }
            using var image = SkiaSharp.SKImage.FromBitmap(bmp);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("PNG encode failed");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var file = File.Create(path);
            data.SaveTo(file);
            return null;
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            gl.DeleteFramebuffer(fbo);
            gl.DeleteRenderbuffer(color);
            gl.DeleteRenderbuffer(depth);
        }
    }

    /// <summary>Offscreen palette thumbnail of a placement prototype (GL thread only).</summary>
    public byte[]? RenderPlacementThumbnail(W3Placement proto, int size) =>
        _gl == null ? null : Placements.RenderThumbnail(proto, size, Compile);

    public void Dispose()
    {
        // GL objects die with the context; nothing to free explicitly here.
        _gl = null;
    }

    // ------------------------------------------------------------------
    // GPU uploads
    // ------------------------------------------------------------------

    private void UploadStaticResources()
    {
        if (_gl == null || _map == null || _assets == null || _layout == null)
            return;
        GL gl = _gl;

        // Ground texture-array banks: 32 sub-cells per ground layer, blight last.
        var layerIds = new List<string>(_map.GroundTilesets) { "__BLIGHT__" };
        _extended.Clear();
        var cells = new List<byte[]>();
        foreach (string id in layerIds)
        {
            RgbaImage atlas = id == "__BLIGHT__"
                ? _assets.LoadBlightAtlas(_map.Tileset)
                : _assets.LoadGroundAtlas(id);
            bool extended = atlas.Width >= atlas.Height * 2;
            _extended.Add(extended);
            for (int sub = 0; sub < GroundLayout.LayerStride; sub++)
                cells.Add(GroundLayout.SliceCell(atlas.Pixels, atlas.Width, atlas.Height, sub, extended));
        }
        // _extended feeds GetVariation → tile data must be rebuilt with it.
        _layout = new GroundLayout(_map, _rules!, _extended, _cliffToGround, layerIds.Count);

        int cell = GroundLayout.CellSize;
        for (int bank = 0; bank < ArrayBankCount; bank++)
        {
            if (_groundBanks[bank] == 0)
                _groundBanks[bank] = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2DArray, _groundBanks[bank]);
            int first = bank * ArrayBankSize;
            int count = Math.Max(1, Math.Min(ArrayBankSize, cells.Count - first));
            unsafe
            {
                gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba8,
                    (uint)cell, (uint)cell, (uint)count, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
            }
            for (int layer = 0; layer < count; layer++)
            {
                byte[] pixels = first + layer < cells.Count ? cells[first + layer] : new byte[cell * cell * 4];
                gl.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, layer,
                    (uint)cell, (uint)cell, 1, PixelFormat.Rgba, PixelType.UnsignedByte, (ReadOnlySpan<byte>)pixels);
            }
            // HiveWE GroundTexture: GL_LINEAR_MIPMAP_LINEAR + full mip chain.
            gl.GenerateMipmap(TextureTarget.Texture2DArray);
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        // Water animation frames + config.
        _waterConfig = _assets.LoadWaterConfig(_map.Tileset);
        List<RgbaImage> frames = _assets.LoadWaterFrames(_waterConfig);
        _waterFrames = frames.Count;
        if (_waterArrayTex == 0)
            _waterArrayTex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, _waterArrayTex);
        int frameCount = Math.Max(1, _waterFrames);
        unsafe
        {
            gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba8,
                128, 128, (uint)frameCount, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }
        for (int i = 0; i < frameCount; i++)
        {
            byte[] pixels = i < frames.Count ? frames[i].Pixels : RgbaImage.Solid(128, 128, 255, 255, 255).Pixels;
            gl.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, i,
                128, 128, 1, PixelFormat.Rgba, PixelType.UnsignedByte, (ReadOnlySpan<byte>)pixels);
        }
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        foreach (uint texture in _cliffTextureCache.Values)
            gl.DeleteTexture(texture);
        _cliffTextureCache.Clear();
    }

    private void UploadDynamicResources()
    {
        if (_gl == null || _map == null || _layout == null || _waterLayout == null)
            return;
        GL gl = _gl;
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        _heights = _layout.BuildHeightMap();
        UploadR32F(ref _heightTex, _map.Width, _map.Height, _heights);
        // HiveWE lights ground from corner_height only (no cliff layers), so
        // cliff steps don't stamp dark/bright squares onto the skirt tiles.
        UploadR32F(ref _normalHeightTex, _map.Width, _map.Height, _layout.BuildNormalHeightMap());

        // R8 is normalized — a raw 1 would read back as 1/255 (< 0.5).
        byte[] exists = _layout.BuildExistsMap();
        for (int i = 0; i < exists.Length; i++)
            exists[i] = exists[i] != 0 ? (byte)255 : (byte)0;
        UploadR8(ref _existsTex, _map.TileWidth, _map.TileHeight, exists);

        float[] tileData = _layout.BuildTileData();
        UploadRgba32F(ref _tileTex, _map.TileWidth, _map.TileHeight, tileData);

        UploadR32F(ref _waterHeightTex, _map.Width, _map.Height, _waterLayout.BuildWaterHeightMap());
        UploadR32F(ref _waterGroundTex, _map.Width, _map.Height, _waterLayout.BuildGroundHeightMap());
        byte[] wet = _waterLayout.BuildExistsMap(out _);
        for (int i = 0; i < wet.Length; i++)
            wet[i] = wet[i] != 0 ? (byte)255 : (byte)0;
        UploadR8(ref _waterExistsTex, _map.TileWidth, _map.TileHeight, wet);

        RebuildCliffBatches();
        StatusLine = $"GL: {_map.TileWidth}x{_map.TileHeight} 瓦片, 悬崖 {_cliffPlacements.Count} 块, 水帧 {_waterFrames}";
    }

    private void UploadR32F(ref uint texture, int width, int height, float[] data)
    {
        GL gl = _gl!;
        if (texture == 0)
            texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R32f, (uint)width, (uint)height, 0,
            PixelFormat.Red, PixelType.Float, (ReadOnlySpan<float>)data);
        SetNearest(gl);
    }

    private void UploadR8(ref uint texture, int width, int height, byte[] data)
    {
        GL gl = _gl!;
        if (texture == 0)
            texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)width, (uint)height, 0,
            PixelFormat.Red, PixelType.UnsignedByte, (ReadOnlySpan<byte>)data);
        SetNearest(gl);
    }

    private void UploadRgba32F(ref uint texture, int width, int height, float[] data)
    {
        GL gl = _gl!;
        if (texture == 0)
            texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.Float, (ReadOnlySpan<float>)data);
        SetNearest(gl);
    }

    private static void SetNearest(GL gl)
    {
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    // ------------------------------------------------------------------
    // Cliffs
    // ------------------------------------------------------------------

    private void RebuildCliffBatches()
    {
        GL gl = _gl!;
        foreach (CliffBatchGpu batch in _cliffGpu)
        {
            gl.DeleteVertexArray(batch.Vao);
            gl.DeleteBuffer(batch.Vbo);
            gl.DeleteBuffer(batch.Ebo);
        }
        _cliffGpu.Clear();
        if (_map == null || _assets == null)
            return;

        // Group placements by cliff texture index → one interleaved batch each.
        var builders = new Dictionary<int, (List<float> Vertices, List<uint> Indices)>();
        foreach (WarsmashTerrainRules.CliffPlacement placement in _cliffPlacements)
        {
            string? modelPath = _assets.ResolveModel(placement.ModelPath);
            if (modelPath == null)
                continue;
            if (!_modelCache.TryGetValue(modelPath, out MdxStaticModel? model))
            {
                model = MdxStaticModel.LoadFile(modelPath);
                _modelCache[modelPath] = model;
            }
            if (model == null)
                continue;
            if (!builders.TryGetValue(placement.CliffTexture, out var builder))
            {
                builder = (new List<float>(), new List<uint>());
                builders[placement.CliffTexture] = builder;
            }
            AppendCliffInstance(builder.Vertices, builder.Indices, model, placement);
        }

        foreach ((int textureIndex, (List<float> vertices, List<uint> indices)) in builders)
        {
            if (indices.Count == 0)
                continue;
            var batch = new CliffBatchGpu
            {
                Vao = gl.GenVertexArray(),
                Vbo = gl.GenBuffer(),
                Ebo = gl.GenBuffer(),
                IndexCount = indices.Count,
                Texture = GetCliffTexture(textureIndex),
            };
            gl.BindVertexArray(batch.Vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, batch.Vbo);
            gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)vertices.ToArray(), BufferUsageARB.StaticDraw);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, batch.Ebo);
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (ReadOnlySpan<uint>)indices.ToArray(), BufferUsageARB.StaticDraw);
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
            _cliffGpu.Add(batch);
        }
    }

    /// <summary>
    /// Transforms one cliff model instance to world space. Mirrors
    /// w3_mdx_instance.gd: Godot vertex = (w3.y, w3.z, -w3.x) (cliff remap),
    /// then Y += bilinear(3x3 GroundHeight grid) * 128, then placement offset
    /// at (BaseLayer - 2) * 128.
    /// </summary>
    private void AppendCliffInstance(
        List<float> vertices, List<uint> indices, MdxStaticModel model, WarsmashTerrainRules.CliffPlacement placement)
    {
        Span<float> grid = stackalloc float[9];
        for (int gy = 0; gy < 3; gy++)
        for (int gx = 0; gx < 3; gx++)
        {
            int cx = Math.Clamp(placement.X + gx, 0, _map!.Width - 1);
            int cy = Math.Clamp(placement.Y + gy, 0, _map.Height - 1);
            grid[gy * 3 + gx] = _map.GetCorner(cx, cy).GroundHeight;
        }
        float baseX = _map!.CenterOffsetX + placement.X * W3TerrainMap.TileSize;
        float baseY = (placement.BaseLayer - 2f) * W3TerrainMap.TileSize;
        float baseZ = _map.CenterOffsetY + placement.Y * W3TerrainMap.TileSize;

        foreach (MdxStaticModel.Geoset geoset in model.Geosets)
        {
            uint indexBase = (uint)(vertices.Count / 8);
            int vertexCount = geoset.Positions.Count / 3;
            for (int i = 0; i < vertexCount; i++)
            {
                float wx = geoset.Positions[i * 3];
                float wy = geoset.Positions[i * 3 + 1];
                float wz = geoset.Positions[i * 3 + 2];
                float gx = wy;           // Godot X
                float gyv = wz;          // Godot Y
                float gz = -wx;          // Godot Z
                gyv += SampleGrid(grid, gx / 128f, gz / 128f) * 128f;
                float nx = geoset.Normals[i * 3 + 1];
                float ny = geoset.Normals[i * 3 + 2];
                float nz = -geoset.Normals[i * 3];
                vertices.Add(baseX + gx);
                vertices.Add(baseY + gyv);
                vertices.Add(baseZ + gz);
                vertices.Add(nx);
                vertices.Add(ny);
                vertices.Add(nz);
                vertices.Add(i * 2 < geoset.Uvs.Count ? geoset.Uvs[i * 2] : 0f);
                vertices.Add(i * 2 + 1 < geoset.Uvs.Count ? geoset.Uvs[i * 2 + 1] : 0f);
            }
            foreach (ushort face in geoset.Faces)
                indices.Add(indexBase + face);
        }
    }

    private static float SampleGrid(Span<float> grid, float u, float v)
    {
        float gx = Math.Clamp(u, 0f, 2f);
        float gz = Math.Clamp(v, 0f, 2f);
        int x0 = Math.Min((int)gx, 1);
        int z0 = Math.Min((int)gz, 1);
        float tx = gx - x0;
        float tz = gz - z0;
        float g0 = grid[z0 * 3 + x0] + (grid[z0 * 3 + x0 + 1] - grid[z0 * 3 + x0]) * tx;
        float g1 = grid[(z0 + 1) * 3 + x0] + (grid[(z0 + 1) * 3 + x0 + 1] - grid[(z0 + 1) * 3 + x0]) * tx;
        return g0 + (g1 - g0) * tz;
    }

    private uint GetCliffTexture(int textureIndex)
    {
        if (_cliffTextureCache.TryGetValue(textureIndex, out uint texture))
            return texture;
        GL gl = _gl!;
        RgbaImage image = _assets!.LoadCliffTexture(_map!, textureIndex);
        texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)image.Width, (uint)image.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (ReadOnlySpan<byte>)image.Pixels);
        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        _cliffTextureCache[textureIndex] = texture;
        return texture;
    }

    // ------------------------------------------------------------------
    // Draw passes
    // ------------------------------------------------------------------

    private void DrawGround(Matrix4x4 vp)
    {
        GL gl = _gl!;
        gl.UseProgram(_groundProgram);
        SetMatrix(_groundProgram, "u_vp", vp);
        SetVec2I(_groundProgram, "u_map_tiles", _map!.TileWidth, _map.TileHeight);
        SetVec2I(_groundProgram, "u_map_corners", _map.Width, _map.Height);
        SetFloat(_groundProgram, "u_tile_size", W3TerrainMap.TileSize);
        SetVec2(_groundProgram, "u_origin", _map.CenterOffsetX, _map.CenterOffsetY);
        SetVec3(_groundProgram, "u_light_dir", LightDirection);
        SetFloat(_groundProgram, "u_ambient", LightAmbient);
        SetFloat(_groundProgram, "u_light_scale", LightScale);
        SetFloat(_groundProgram, "u_grid", ShowGrid ? 1f : 0f);
        SetVec3(_groundProgram, "u_cam_target", Camera.Target);
        BindTexture(gl, _groundProgram, 0, "u_height", TextureTarget.Texture2D, _heightTex);
        BindTexture(gl, _groundProgram, 1, "u_exists", TextureTarget.Texture2D, _existsTex);
        BindTexture(gl, _groundProgram, 2, "u_tile_data", TextureTarget.Texture2D, _tileTex);
        BindTexture(gl, _groundProgram, 3, "u_bank0", TextureTarget.Texture2DArray, _groundBanks[0]);
        BindTexture(gl, _groundProgram, 4, "u_bank1", TextureTarget.Texture2DArray, _groundBanks[1]);
        BindTexture(gl, _groundProgram, 5, "u_bank2", TextureTarget.Texture2DArray, _groundBanks[2]);
        BindTexture(gl, _groundProgram, 6, "u_height_normal", TextureTarget.Texture2D, _normalHeightTex);
        gl.BindVertexArray(_groundVao);
        gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, (uint)(_map.TileWidth * _map.TileHeight));
        gl.BindVertexArray(0);
    }

    private void DrawCliffs(Matrix4x4 vp)
    {
        if (_cliffGpu.Count == 0)
            return;
        GL gl = _gl!;
        gl.UseProgram(_cliffProgram);
        SetMatrix(_cliffProgram, "u_vp", vp);
        SetVec3(_cliffProgram, "u_light_dir", LightDirection);
        SetFloat(_cliffProgram, "u_ambient", LightAmbient);
        SetFloat(_cliffProgram, "u_light_scale", LightScale);
        foreach (CliffBatchGpu batch in _cliffGpu)
        {
            BindTexture(gl, _cliffProgram, 0, "u_texture", TextureTarget.Texture2D, batch.Texture);
            gl.BindVertexArray(batch.Vao);
            unsafe { gl.DrawElements(PrimitiveType.Triangles, (uint)batch.IndexCount, DrawElementsType.UnsignedInt, null); }
        }
        gl.BindVertexArray(0);
    }

    private void DrawWater(Matrix4x4 vp, double timeSeconds)
    {
        GL gl = _gl!;
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.DepthMask(false);
        gl.UseProgram(_waterProgram);
        SetMatrix(_waterProgram, "u_vp", vp);
        SetVec2I(_waterProgram, "u_map_tiles", _map!.TileWidth, _map.TileHeight);
        SetVec2I(_waterProgram, "u_map_corners", _map.Width, _map.Height);
        SetFloat(_waterProgram, "u_tile_size", W3TerrainMap.TileSize);
        SetVec2(_waterProgram, "u_origin", _map.CenterOffsetX, _map.CenterOffsetY);
        SetFloat(_waterProgram, "u_height_offset", _waterConfig.HeightOffset);
        SetFloat(_waterProgram, "u_time", (float)timeSeconds);
        SetFloat(_waterProgram, "u_tex_rate", _waterConfig.TextureRate);
        SetInt(_waterProgram, "u_tex_count", _waterFrames);
        SetVec4(_waterProgram, "u_shallow_min", _waterConfig.ShallowMin);
        SetVec4(_waterProgram, "u_shallow_max", _waterConfig.ShallowMax);
        SetVec4(_waterProgram, "u_deep_min", _waterConfig.DeepMin);
        SetVec4(_waterProgram, "u_deep_max", _waterConfig.DeepMax);
        BindTexture(gl, _waterProgram, 0, "u_water_height", TextureTarget.Texture2D, _waterHeightTex);
        BindTexture(gl, _waterProgram, 1, "u_ground_height", TextureTarget.Texture2D, _waterGroundTex);
        BindTexture(gl, _waterProgram, 2, "u_exists", TextureTarget.Texture2D, _waterExistsTex);
        BindTexture(gl, _waterProgram, 3, "u_frames", TextureTarget.Texture2DArray, _waterArrayTex);
        gl.BindVertexArray(_waterVao);
        gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, (uint)(_map.TileWidth * _map.TileHeight));
        gl.BindVertexArray(0);
        gl.DepthMask(true);
        gl.Disable(EnableCap.Blend);
    }

    private void DrawBrushRing(Matrix4x4 vp)
    {
        if (BrushRing == null || _map == null)
            return;
        (float cx, float cy, float radius) = BrushRing.Value;
        const int segments = 64;
        var points = new float[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * MathF.Tau / segments;
            float px = cx + MathF.Cos(angle) * radius;
            float py = cy + MathF.Sin(angle) * radius;
            points[i * 3] = _map.CenterOffsetX + px * W3TerrainMap.TileSize;
            points[i * 3 + 1] = SampleHeight(px, py) + 6f;
            points[i * 3 + 2] = _map.CenterOffsetY + py * W3TerrainMap.TileSize;
        }
        DrawRing(vp, points, new[] { 1f, 0.82f, 0.25f, 0.95f });
    }

    private void DrawRegionRings(Matrix4x4 vp)
    {
        if (_map == null || Regions.Count == 0)
            return;
        foreach (W3RegionRecord region in Regions)
        {
            float left = Math.Min(region.Left, region.Right);
            float right = Math.Max(region.Left, region.Right);
            float bottom = Math.Min(region.Bottom, region.Top);
            float top = Math.Max(region.Bottom, region.Top);
            int horizontal = Math.Max(2, (int)MathF.Ceiling((right - left) / 32f));
            int vertical = Math.Max(2, (int)MathF.Ceiling((top - bottom) / 32f));
            var points = new float[(horizontal * 2 + vertical * 2) * 3];
            int at = 0;
            void Add(float worldX, float worldZ)
            {
                points[at++] = worldX;
                points[at++] = SampleHeightWorld(worldX, worldZ) + 8f;
                points[at++] = worldZ;
            }
            for (int i = 0; i < horizontal; i++) Add(left + (right - left) * i / horizontal, bottom);
            for (int i = 0; i < vertical; i++) Add(right, bottom + (top - bottom) * i / vertical);
            for (int i = 0; i < horizontal; i++) Add(right - (right - left) * i / horizontal, top);
            for (int i = 0; i < vertical; i++) Add(left, top - (top - bottom) * i / vertical);
            DrawRing(vp, points, new[]
            {
                region.R / 255f, region.G / 255f, region.B / 255f, Math.Max(region.A / 255f, 0.35f),
            });
        }
    }

    /// <summary>WE-style selection circles under selected doodads/units.</summary>
    private void DrawSelectionRings(Matrix4x4 vp)
    {
        if (_map == null || Placements.Selection.Count == 0)
            return;
        const int segments = 40;
        var points = new float[segments * 3];
        foreach (W3Placement p in Placements.Selection)
        {
            Vector3? center = Placements.CenterOf(p);
            if (center == null)
                continue;
            float radius = Placements.RadiusOf(p);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * MathF.Tau / segments;
                float wx = center.Value.X + MathF.Cos(angle) * radius;
                float wz = center.Value.Z + MathF.Sin(angle) * radius;
                points[i * 3] = wx;
                points[i * 3 + 1] = SampleHeightWorld(wx, wz) + 5f;
                points[i * 3 + 2] = wz;
            }
            DrawRing(vp, points, new[] { 0.35f, 1f, 0.45f, 0.95f });
        }
    }

    private void DrawRing(Matrix4x4 vp, float[] points, float[] color)
    {
        GL gl = _gl!;
        int segments = points.Length / 3;
        gl.UseProgram(_lineProgram);
        SetMatrix(_lineProgram, "u_vp", vp);
        SetVec4(_lineProgram, "u_color", color);
        gl.Disable(EnableCap.DepthTest);
        gl.BindVertexArray(_ringVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _ringVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)points, BufferUsageARB.StreamDraw);
        gl.DrawArrays(PrimitiveType.LineLoop, 0, (uint)segments);
        gl.BindVertexArray(0);
        gl.Enable(EnableCap.DepthTest);
    }

    // ------------------------------------------------------------------
    // Programs
    // ------------------------------------------------------------------

    private void BuildQuad()
    {
        GL gl = _gl!;
        float[] quad =
        {
            0f, 0f,  1f, 0f,  0f, 1f,
            1f, 0f,  1f, 1f,  0f, 1f,
        };
        _quadVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (ReadOnlySpan<float>)quad, BufferUsageARB.StaticDraw);

        _groundVao = gl.GenVertexArray();
        gl.BindVertexArray(_groundVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        gl.EnableVertexAttribArray(0);
        unsafe { gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 8, null); }

        _waterVao = gl.GenVertexArray();
        gl.BindVertexArray(_waterVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        gl.EnableVertexAttribArray(0);
        unsafe { gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 8, null); }
        gl.BindVertexArray(0);
    }

    private void BuildPrograms()
    {
        _groundProgram = Compile(GroundVertexSource, GroundFragmentSource);
        _waterProgram = Compile(WaterVertexSource, WaterFragmentSource);
        _cliffProgram = Compile(CliffVertexSource, CliffFragmentSource);
        _lineProgram = Compile(LineVertexSource, LineFragmentSource);
    }

    private string Header => _es
        ? "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp sampler2DArray;\n"
        : "#version 330 core\n";

    private uint Compile(string vertexBody, string fragmentBody)
    {
        GL gl = _gl!;
        uint vs = CompileShader(ShaderType.VertexShader, Header + vertexBody);
        uint fs = CompileShader(ShaderType.FragmentShader, Header + fragmentBody);
        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException("GL link error: " + gl.GetProgramInfoLog(program));
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return program;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        GL gl = _gl!;
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException($"GL {type} error: {gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    private void SetMatrix(uint program, string name, Matrix4x4 matrix)
    {
        // System.Numerics row-major + no transpose == GLSL M * v matching v * M.
        Span<float> data = stackalloc float[16]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        };
        GL gl = _gl!;
        gl.UniformMatrix4(gl.GetUniformLocation(program, name), 1, false, data);
    }

    private void SetFloat(uint program, string name, float value) =>
        _gl!.Uniform1(_gl.GetUniformLocation(program, name), value);

    private void SetInt(uint program, string name, int value) =>
        _gl!.Uniform1(_gl.GetUniformLocation(program, name), value);

    private void SetVec2(uint program, string name, float x, float y) =>
        _gl!.Uniform2(_gl.GetUniformLocation(program, name), x, y);

    private void SetVec2I(uint program, string name, int x, int y) =>
        _gl!.Uniform2(_gl.GetUniformLocation(program, name), x, y);

    private void SetVec3(uint program, string name, Vector3 value) =>
        _gl!.Uniform3(_gl.GetUniformLocation(program, name), value.X, value.Y, value.Z);

    private void SetVec4(uint program, string name, float[] value) =>
        _gl!.Uniform4(_gl.GetUniformLocation(program, name), value[0], value[1], value[2], value[3]);

    private static void BindTexture(GL gl, uint program, int unit, string name, TextureTarget target, uint texture)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + unit);
        gl.BindTexture(target, texture);
        gl.Uniform1(gl.GetUniformLocation(program, name), unit);
    }

    // ------------------------------------------------------------------
    // GLSL — ports of w3_ground_unified.gdshader / w3_gpu_water.gdshader
    // ------------------------------------------------------------------

    private const string GroundVertexSource = @"
layout(location = 0) in vec2 a_local;
uniform sampler2D u_height;
uniform sampler2D u_height_normal;
uniform sampler2D u_exists;
uniform ivec2 u_map_tiles;
uniform ivec2 u_map_corners;
uniform float u_tile_size;
uniform vec2 u_origin;
uniform mat4 u_vp;
out vec2 v_local;
flat out ivec2 v_tile;
out vec3 v_normal;
out vec3 v_world;

float sample_height(ivec2 c) {
    c = clamp(c, ivec2(0), u_map_corners - ivec2(1));
    return texelFetch(u_height, c, 0).r;
}

// HiveWE terrain.vert: normals come from ground_heights (corner_height only,
// no cliff layers) so cliff steps never shade the surrounding ground tiles.
float sample_nheight(ivec2 c) {
    c = clamp(c, ivec2(0), u_map_corners - ivec2(1));
    return texelFetch(u_height_normal, c, 0).r;
}

void main() {
    int id = gl_InstanceID;
    int tile_x = id % u_map_tiles.x;
    int tile_y = id / u_map_tiles.x;
    v_tile = ivec2(tile_x, tile_y);
    vec2 local = a_local;
    v_local = local;
    float exists = texelFetch(u_exists, v_tile, 0).r;
    float h00 = sample_height(ivec2(tile_x, tile_y));
    float h10 = sample_height(ivec2(tile_x + 1, tile_y));
    float h01 = sample_height(ivec2(tile_x, tile_y + 1));
    float h11 = sample_height(ivec2(tile_x + 1, tile_y + 1));
    float h = mix(mix(h00, h10, local.x), mix(h01, h11, local.x), local.y);
    vec3 world = vec3(
        u_origin.x + (float(tile_x) + local.x) * u_tile_size,
        exists < 0.5 ? -100000.0 : h,
        u_origin.y + (float(tile_y) + local.y) * u_tile_size);
    ivec2 cc = v_tile + ivec2(int(local.x + 0.5), int(local.y + 0.5));
    float hl = sample_nheight(cc + ivec2(-1, 0));
    float hr = sample_nheight(cc + ivec2(1, 0));
    float hd = sample_nheight(cc + ivec2(0, -1));
    float hu = sample_nheight(cc + ivec2(0, 1));
    v_normal = normalize(vec3(hl - hr, 2.0 * u_tile_size, hd - hu));
    v_world = world;
    gl_Position = u_vp * vec4(world, 1.0);
}
";

    private const string GroundFragmentSource = @"
in vec2 v_local;
flat in ivec2 v_tile;
in vec3 v_normal;
in vec3 v_world;
uniform sampler2D u_exists;
uniform sampler2D u_tile_data;
uniform sampler2DArray u_bank0;
uniform sampler2DArray u_bank1;
uniform sampler2DArray u_bank2;
uniform vec3 u_light_dir;
uniform float u_ambient;
uniform float u_light_scale;
uniform float u_grid;
uniform vec3 u_cam_target;
out vec4 o_color;

const float EMPTY = 60000.0;
const float BANK = 256.0;

vec4 sample_layer(float layer_f, vec2 local) {
    if (layer_f > EMPTY - 1.0) return vec4(0.0);
    vec2 uv = vec2(local.x, 1.0 - local.y);
    if (layer_f < BANK) return texture(u_bank0, vec3(uv, layer_f));
    if (layer_f < BANK * 2.0) return texture(u_bank1, vec3(uv, layer_f - BANK));
    return texture(u_bank2, vec3(uv, layer_f - BANK * 2.0));
}

void main() {
    float exists = texelFetch(u_exists, v_tile, 0).r;
    if (exists < 0.5) discard;
    vec4 ti = texelFetch(u_tile_data, v_tile, 0);
    // HiveWE terrain.frag: start at the TOPMOST overlay and under-composite
    // down to the base (order matters for 3+ layer tiles).
    vec4 col = sample_layer(ti.a, v_local);
    col = mix(sample_layer(ti.b, v_local), col, col.a);
    col = mix(sample_layer(ti.g, v_local), col, col.a);
    col = mix(sample_layer(ti.r, v_local), col, col.a);
    float ndotl = max(dot(normalize(v_normal), normalize(u_light_dir)), 0.0);
    float light = min(1.0, mix(u_ambient, 1.0, ndotl) * u_light_scale);
    vec3 rgb = col.rgb * light;
    if (u_grid > 0.5) {
        // Thin white tile-boundary lines (WE-style). Width is capped in tile
        // space: on cliff-skirt tiles v_local is compressed on screen, so raw
        // fwidth would flood-fill the whole tile white (the ""pale squares"").
        vec2 fw = fwidth(v_local);
        vec2 dist = min(v_local, 1.0 - v_local);
        vec2 aa = smoothstep(vec2(0.0), min(fw * 1.4, vec2(0.05)), dist);
        float line = 1.0 - min(aa.x, aa.y);
        float range = length(v_world.xz - u_cam_target.xz) / 128.0;
        float fade = 1.0 - smoothstep(24.0, 40.0, range);
        rgb = mix(rgb, vec3(1.0), line * 0.275 * fade);
    }
    o_color = vec4(rgb, 1.0);
}
";

    private const string WaterVertexSource = @"
layout(location = 0) in vec2 a_local;
uniform sampler2D u_water_height;
uniform sampler2D u_ground_height;
uniform sampler2D u_exists;
uniform ivec2 u_map_tiles;
uniform ivec2 u_map_corners;
uniform float u_tile_size;
uniform vec2 u_origin;
uniform float u_height_offset;
uniform mat4 u_vp;
out vec2 v_uv;
out float v_depth;

float fetch(sampler2D tex, ivec2 c) {
    c = clamp(c, ivec2(0), u_map_corners - ivec2(1));
    return texelFetch(tex, c, 0).r;
}

void main() {
    int id = gl_InstanceID;
    int tile_x = id % u_map_tiles.x;
    int tile_y = id / u_map_tiles.x;
    vec2 local = a_local;
    v_uv = local;
    float exists = texelFetch(u_exists, ivec2(tile_x, tile_y), 0).r;
    float w00 = fetch(u_water_height, ivec2(tile_x, tile_y));
    float w10 = fetch(u_water_height, ivec2(tile_x + 1, tile_y));
    float w01 = fetch(u_water_height, ivec2(tile_x, tile_y + 1));
    float w11 = fetch(u_water_height, ivec2(tile_x + 1, tile_y + 1));
    float wh = mix(mix(w00, w10, local.x), mix(w01, w11, local.x), local.y);
    float g00 = fetch(u_ground_height, ivec2(tile_x, tile_y));
    float g10 = fetch(u_ground_height, ivec2(tile_x + 1, tile_y));
    float g01 = fetch(u_ground_height, ivec2(tile_x, tile_y + 1));
    float g11 = fetch(u_ground_height, ivec2(tile_x + 1, tile_y + 1));
    float gh = mix(mix(g00, g10, local.x), mix(g01, g11, local.x), local.y);
    v_depth = max(wh + u_height_offset - gh, 0.0);
    vec3 world = vec3(
        u_origin.x + (float(tile_x) + local.x) * u_tile_size,
        exists < 0.5 ? -100000.0 : (wh + u_height_offset) * u_tile_size,
        u_origin.y + (float(tile_y) + local.y) * u_tile_size);
    gl_Position = u_vp * vec4(world, 1.0);
}
";

    private const string WaterFragmentSource = @"
in vec2 v_uv;
in float v_depth;
uniform sampler2DArray u_frames;
uniform int u_tex_count;
uniform float u_tex_rate;
uniform float u_time;
uniform vec4 u_shallow_min;
uniform vec4 u_shallow_max;
uniform vec4 u_deep_min;
uniform vec4 u_deep_max;
out vec4 o_color;

void main() {
    const float min_depth = 10.0 / 128.0;
    const float deep_level = 64.0 / 128.0;
    const float max_depth = 72.0 / 128.0;
    vec4 tint;
    if (v_depth <= deep_level) {
        float value = max(0.0, v_depth - min_depth) / (deep_level - min_depth);
        tint = mix(u_shallow_min, u_shallow_max, clamp(value, 0.0, 1.0));
    } else {
        float value = clamp(v_depth - deep_level, 0.0, max_depth - deep_level) / (max_depth - deep_level);
        tint = mix(u_deep_min, u_deep_max, value);
    }
    vec4 frame = vec4(1.0);
    if (u_tex_count > 0) {
        float layer = mod(floor(u_time * u_tex_rate), float(u_tex_count));
        frame = texture(u_frames, vec3(v_uv.x, 1.0 - v_uv.y, layer));
    }
    vec4 color = frame * tint;
    o_color = vec4(color.rgb, color.a);
}
";

    private const string CliffVertexSource = @"
layout(location = 0) in vec3 a_position;
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_uv;
uniform mat4 u_vp;
out vec3 v_normal;
out vec2 v_uv;

void main() {
    v_normal = a_normal;
    v_uv = a_uv;
    gl_Position = u_vp * vec4(a_position, 1.0);
}
";

    private const string CliffFragmentSource = @"
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

    private const string LineVertexSource = @"
layout(location = 0) in vec3 a_position;
uniform mat4 u_vp;
void main() { gl_Position = u_vp * vec4(a_position, 1.0); }
";

    private const string LineFragmentSource = @"
uniform vec4 u_color;
out vec4 o_color;
void main() { o_color = u_color; }
";
}
