using Godot;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed partial class RewardPoolBuilderSession
{
    // These are original Godot visuals drawn from the reference's geometry and
    // palette. They do not load MDPro3 textures, materials, fonts, or code.
    private static readonly Color ManagerVisualGreen = new(0.68f, 1f, 0f, 1f);
    private static readonly Color ManagerVisualWhite = new(0.97f, 0.98f, 1f, 1f);
    private static readonly Color ManagerVisualBorder = new(0.60f, 0.63f, 0.62f, 1f);

    private static Control CreateManagerBackground()
    {
        var background = new ColorRect
        {
            Name = "ThermalVortexManagerBlueBackground",
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
            Material = new ShaderMaterial
            {
                Shader = new Shader { Code = ManagerVisualBackgroundShader }
            }
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return background;
    }

    private static Button CreateManagerButton(string text, Vector2 minimum, bool cut = false)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = minimum,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        button.AddThemeFontSizeOverride("font_size", 28);
        button.AddThemeColorOverride("font_color", ManagerVisualWhite);
        button.AddThemeColorOverride("font_hover_color", ManagerVisualGreen);
        button.AddThemeColorOverride("font_pressed_color", ManagerVisualGreen);
        button.AddThemeColorOverride("font_focus_color", ManagerVisualGreen);
        button.AddThemeColorOverride("font_disabled_color", new Color(0.48f, 0.51f, 0.52f));
        button.AddThemeStyleboxOverride("normal", ManagerVisualButtonStyle(false, false, false, cut));
        button.AddThemeStyleboxOverride("hover", ManagerVisualButtonStyle(true, false, false, cut));
        button.AddThemeStyleboxOverride("pressed", ManagerVisualButtonStyle(true, true, false, cut));
        button.AddThemeStyleboxOverride("disabled", ManagerVisualButtonStyle(false, false, true, cut));
        button.AddThemeStyleboxOverride("focus", ManagerVisualFocusStyle(cut));
        return button;
    }

    private static Button CreateManagerBackButton()
    {
        var button = CreateManagerButton(string.Empty, new Vector2(60f, 60f));
        button.Name = "ThermalVortexManagerBack";
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var hovered = state is "hover" or "pressed";
            var style = ManagerVisualButtonStyle(hovered, state == "pressed", state == "disabled", false);
            style.CornerRadiusTopLeft = 30;
            style.CornerRadiusTopRight = 30;
            style.CornerRadiusBottomLeft = 30;
            style.CornerRadiusBottomRight = 30;
            style.CornerDetail = 16;
            style.ContentMarginLeft = 0f;
            style.ContentMarginRight = 0f;
            style.ContentMarginTop = 0f;
            style.ContentMarginBottom = 0f;
            button.AddThemeStyleboxOverride(state, style);
        }
        var focus = ManagerVisualFocusStyle(false);
        focus.CornerRadiusTopLeft = 30;
        focus.CornerRadiusTopRight = 30;
        focus.CornerRadiusBottomLeft = 30;
        focus.CornerRadiusBottomRight = 30;
        focus.CornerDetail = 16;
        button.AddThemeStyleboxOverride("focus", focus);
        var arrow = new Polygon2D
        {
            Name = "BackArrow",
            Color = ManagerVisualGreen,
            Antialiased = true,
            Polygon =
            [
                new Vector2(39f, 13f), new Vector2(16f, 30f), new Vector2(39f, 47f),
                new Vector2(39f, 36f), new Vector2(29f, 30f), new Vector2(39f, 24f)
            ]
        };
        button.AddChild(arrow);
        return button;
    }

    private static Control CreateManagerAddGlyph()
    {
        var host = new Control
        {
            Name = "ThermalVortexManagerAddGlyph",
            CustomMinimumSize = new Vector2(100f, 100f),
            Size = new Vector2(100f, 100f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None
        };
        var circle = new Line2D
        {
            Name = "AddCircle",
            Width = 3.2f,
            DefaultColor = ManagerVisualGreen,
            Antialiased = true
        };
        var points = new Vector2[97];
        for (var index = 0; index < points.Length; index++)
        {
            var angle = Mathf.Tau * index / (points.Length - 1);
            points[index] = new Vector2(50f, 50f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 47f;
        }
        circle.Points = points;
        host.AddChild(circle);
        host.AddChild(ManagerVisualLine("AddHorizontal", [new Vector2(27f, 50f), new Vector2(73f, 50f)], ManagerVisualGreen, 3.5f));
        host.AddChild(ManagerVisualLine("AddVertical", [new Vector2(50f, 27f), new Vector2(50f, 73f)], ManagerVisualGreen, 3.5f));
        return host;
    }

    private static StyleBoxFlat CreateManagerTileStyle(bool hovered, bool selected, bool disabled = false)
    {
        var highlighted = (hovered || selected) && !disabled;
        return new StyleBoxFlat
        {
            BgColor = disabled ? new Color(0.035f, 0.045f, 0.05f, 0.97f) : new Color(0f, 0f, 0f, 0.99f),
            BorderColor = disabled ? new Color(0.31f, 0.35f, 0.36f) : highlighted ? ManagerVisualGreen : ManagerVisualBorder,
            BorderWidthLeft = highlighted ? 2 : 1,
            BorderWidthTop = highlighted ? 2 : 1,
            BorderWidthRight = highlighted ? 2 : 1,
            BorderWidthBottom = highlighted ? 2 : 1,
            CornerRadiusTopLeft = 20,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 20,
            CornerDetail = 1,
            AntiAliasing = true,
            ContentMarginLeft = 10f,
            ContentMarginRight = 10f,
            ContentMarginTop = 10f,
            ContentMarginBottom = 10f
        };
    }

    private static Control CreateManagerFooterShape()
    {
        var host = new Control
        {
            Name = "ThermalVortexManagerFooterShape",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None
        };
        host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var fill = new Polygon2D
        {
            Name = "FooterCutPanel",
            Color = new Color(0.015f, 0.10f, 0.16f, 0.68f),
            Antialiased = true
        };
        var line = ManagerVisualLine("FooterCutEdge", [], new Color(0.49f, 0.53f, 0.50f, 0.88f), 1.5f);
        host.AddChild(fill);
        host.AddChild(line);
        void UpdateGeometry()
        {
            var size = host.Size;
            if (size.X <= 0f || size.Y <= 0f) return;
            // Coordinates follow the 1920 x 1080 design canvas supplied by the
            // manager. The left horizontal strip remains thin beneath the grid.
            var lower = size.Y - 18f;
            var upper = size.Y - 140f;
            var start = size.X * 0.34f;
            var corner = start + 122f;
            fill.Polygon =
            [
                new Vector2(0f, lower), new Vector2(start, lower), new Vector2(corner, upper),
                new Vector2(size.X, upper), new Vector2(size.X, size.Y), new Vector2(0f, size.Y)
            ];
            line.Points =
            [
                new Vector2(0f, lower), new Vector2(start, lower),
                new Vector2(corner, upper), new Vector2(size.X, upper)
            ];
        }
        host.Ready += UpdateGeometry;
        host.Resized += UpdateGeometry;
        return host;
    }

    private static StyleBoxFlat ManagerVisualButtonStyle(bool hovered, bool pressed, bool disabled, bool cut) => new()
    {
        BgColor = disabled ? new Color(0.055f, 0.07f, 0.075f, 0.96f)
            : pressed ? new Color(0.035f, 0.09f, 0.04f, 0.98f) : new Color(0f, 0f, 0f, 0.99f),
        BorderColor = disabled ? new Color(0.31f, 0.35f, 0.36f)
            : hovered ? ManagerVisualGreen : ManagerVisualBorder,
        BorderWidthLeft = hovered ? 2 : 1,
        BorderWidthTop = hovered ? 2 : 1,
        BorderWidthRight = hovered ? 2 : 1,
        BorderWidthBottom = hovered ? 2 : 1,
        CornerRadiusTopLeft = cut ? 20 : 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = cut ? 20 : 6,
        CornerDetail = cut ? 1 : 8,
        AntiAliasing = true,
        ContentMarginLeft = 18f,
        ContentMarginRight = 18f,
        ContentMarginTop = 5f,
        ContentMarginBottom = 5f
    };

    private static StyleBoxFlat ManagerVisualFocusStyle(bool cut) => new()
    {
        BgColor = new Color(0f, 0f, 0f, 0f),
        BorderColor = ManagerVisualGreen,
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = cut ? 20 : 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = cut ? 20 : 6,
        CornerDetail = cut ? 1 : 8,
        AntiAliasing = true
    };

    private static Line2D ManagerVisualLine(string name, Vector2[] points, Color color, float width) => new()
    {
        Name = name,
        Points = points,
        DefaultColor = color,
        Width = width,
        Antialiased = true
    };

    private const string ManagerVisualBackgroundShader = """
        shader_type canvas_item;
        render_mode unshaded;

        float manager_hash(vec2 p) {
            p = fract(p * vec2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return fract(p.x * p.y);
        }

        float manager_beam(float x, float center, float width) {
            float distance_from_beam = abs(x - center) / width;
            return exp(-distance_from_beam * distance_from_beam);
        }

        void fragment() {
            vec2 uv = UV;
            vec2 pixel = FRAGCOORD.xy;
            float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
            float column = floor(uv.x * 270.0);
            float random_column = manager_hash(vec2(column, 6.0));
            float thread = pow(1.0 - abs(fract(uv.x * 270.0) * 2.0 - 1.0), 2.3);
            float vertical_variation = 0.55 + 0.45 * sin(uv.y * 15.0 + random_column * 31.0);
            float bars = thread * (0.13 + 0.45 * random_column) * vertical_variation;
            float secondary_bars = pow(0.5 + 0.5 * sin(uv.x * 1880.0), 6.0) * 0.10;
            vec3 color = vec3(0.006, 0.022, 0.053);
            color += vec3(0.015, 0.16, 0.32) * (bars + secondary_bars);
            color += vec3(0.00, 0.06, 0.12) * (0.5 + 0.5 * sin(uv.x * 84.0 + 1.0));

            float cyan = manager_beam(uv.x, 0.262, 0.0038) * (0.65 + uv.y * 0.80);
            cyan += manager_beam(uv.x, 0.447, 0.0045) * (0.96 - uv.y * 0.80);
            cyan += manager_beam(uv.x, 0.186, 0.0019) * 0.30;
            cyan += manager_beam(uv.x, 0.091, 0.0013) * uv.y * 0.55;
            color += vec3(0.06, 0.88, 1.0) * cyan;
            float cyan_glow = manager_beam(uv.x, 0.262, 0.017) * 0.24;
            cyan_glow += manager_beam(uv.x, 0.447, 0.018) * (1.0 - uv.y) * 0.27;
            color += vec3(0.00, 0.32, 0.50) * cyan_glow;

            vec2 grain_cell = floor(vec2(uv.x * 176.0, uv.y * 105.0));
            float grain = manager_hash(grain_cell);
            color += vec3(0.01, 0.035, 0.06) * grain;
            float scan = 0.78 + 0.22 * sin(pixel.y * 1.65);
            color *= scan;

            // Sparse original cyan and violet pinpoints, independent of the
            // underlying columns. Aspect correction keeps their shape circular.
            vec2 spark_grid = vec2(uv.x * 58.0, uv.y * 34.0);
            vec2 spark_cell = floor(spark_grid);
            float spark_seed = manager_hash(spark_cell + vec2(19.0, 7.0));
            vec2 spark_offset = vec2(manager_hash(spark_cell + 31.0), manager_hash(spark_cell + 53.0));
            float spark_distance = length((fract(spark_grid) - spark_offset) * vec2(aspect / 1.7, 1.0));
            float spark = exp(-spark_distance * spark_distance * 2600.0) * step(0.94, spark_seed);
            color += mix(vec3(0.00, 0.28, 0.98), vec3(0.38, 0.10, 0.76), step(0.975, spark_seed)) * spark;

            for (int i = 0; i < 18; i++) {
                float index = float(i);
                vec2 center = vec2(manager_hash(vec2(index, 71.0)), manager_hash(vec2(index, 84.0)));
                float radius = 0.007 + manager_hash(vec2(index, 95.0)) * 0.011;
                float distance_from_dot = length((uv - center) * vec2(aspect, 1.0));
                float disc = 1.0 - smoothstep(radius * 0.90, radius, distance_from_dot);
                color += mix(vec3(0.09, 0.17, 0.29), vec3(0.16, 0.055, 0.27), step(0.4, fract(index * 0.618))) * disc;
            }
            float vignette = 1.0 - 0.27 * pow(length((uv - 0.5) * vec2(1.0, 0.65)), 1.3);
            COLOR = vec4(color * vignette, 1.0);
        }
        """;
}
