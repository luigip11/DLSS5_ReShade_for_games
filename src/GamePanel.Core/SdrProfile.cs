using System.Globalization;
using System.Text;

namespace GamePanel.Core;

public sealed record SdrSettings(bool Enabled, int Sharpness, int Color, int Intensity,
    int Contrast = 0, int Bloom = 0, int Vignette = 0)
{
    public static SdrSettings Balanced => new(true, 35, 58, 70);
    public static SdrSettings Original => new(false, 0, 50, 0);
    public static SdrSettings Cinema => new(true, 45, 62, 90, 35, 30, 18);
    public void Validate()
    {
        if (Sharpness is < 0 or > 100 || Color is < 0 or > 100 || Intensity is < 0 or > 100
            || Contrast is < 0 or > 100 || Bloom is < 0 or > 100 || Vignette is < 0 or > 100)
            throw new InvalidDataException("I parametri immagine devono essere tra 0 e 100.");
    }
}

public static class SdrProfile
{
    public const string ShaderPath = "GamePanelFX/GamePanelSDR.fx";
    public const string PresetPath = "GamePanelPreset.ini";
    public const string ConfigPath = "ReShade.ini";
    public const string LoaderPath = "d3d9.dll";
    public static readonly IReadOnlyList<string> Targets = Array.AsReadOnly(new[] { LoaderPath, ConfigPath, PresetPath, ShaderPath });
    public static byte[] Preset(SdrSettings settings)
    {
        settings.Validate();
        string F(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
        return Encoding.UTF8.GetBytes($"Techniques={(settings.Enabled ? "GamePanelSDR@GamePanelSDR.fx" : "")}\r\nTechniqueSorting=GamePanelSDR@GamePanelSDR.fx\r\n\r\n[GamePanelSDR.fx]\r\nSharpness={F(settings.Sharpness / 100d)}\r\nSaturation={F(0.5 + settings.Color / 100d)}\r\nStrength={F(settings.Intensity / 100d)}\r\nContrast={F(settings.Contrast / 100d)}\r\nBloom={F(settings.Bloom / 100d)}\r\nVignette={F(settings.Vignette / 100d)}\r\n");
    }
    public static byte[] Configuration => Encoding.UTF8.GetBytes("[GENERAL]\r\nEffectSearchPaths=.\\GamePanelFX\r\nTextureSearchPaths=.\\GamePanelFX\r\nPresetPath=.\\GamePanelPreset.ini\r\nStartupPresetPath=.\\GamePanelPreset.ini\r\nPerformanceMode=1\r\nNoEffectCache=1\r\n\r\n[INPUT]\r\nKeyOverlay=36,0,0,0\r\nKeyEffects=145,0,0,0\r\n\r\n[OVERLAY]\r\nTutorialProgress=4\r\n");
    // Original MIT-licensed effect. No external include, texture or depth buffer required.
    public const string Shader = """
        // GamePanel contributors — MIT. SDR-only, no depth access.
        uniform float Sharpness < ui_type = "slider"; ui_min = 0.0; ui_max = 1.0; > = 0.35;
        uniform float Saturation < ui_type = "slider"; ui_min = 0.5; ui_max = 1.5; > = 1.08;
        uniform float Strength < ui_type = "slider"; ui_min = 0.0; ui_max = 1.0; > = 0.70;
        uniform float Contrast < ui_type = "slider"; ui_min = 0.0; ui_max = 1.0; > = 0.0;
        uniform float Bloom < ui_type = "slider"; ui_min = 0.0; ui_max = 1.0; > = 0.0;
        uniform float Vignette < ui_type = "slider"; ui_min = 0.0; ui_max = 1.0; > = 0.0;
        texture2D GamePanelColor : COLOR;
        sampler2D GamePanelSampler { Texture = GamePanelColor; AddressU = CLAMP; AddressV = CLAMP; };
        float3 GamePanelHighlights(float2 uv)
        {
            float3 c = tex2D(GamePanelSampler, uv).rgb;
            float brightness = max(c.r, max(c.g, c.b));
            return c * smoothstep(0.60, 1.0, brightness);
        }
        void GamePanelVS(uint id : SV_VertexID, out float4 position : SV_Position, out float2 uv : TEXCOORD)
        {
            uv.x = (id == 2) ? 2.0 : 0.0;
            uv.y = (id == 1) ? 2.0 : 0.0;
            position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
        }
        float4 GamePanelPS(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target
        {
            float2 px = float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT);
            float3 original = tex2D(GamePanelSampler, uv).rgb;
            float3 neighbors = tex2D(GamePanelSampler, uv + float2(px.x, 0.0)).rgb;
            neighbors += tex2D(GamePanelSampler, uv - float2(px.x, 0.0)).rgb;
            neighbors += tex2D(GamePanelSampler, uv + float2(0.0, px.y)).rgb;
            neighbors += tex2D(GamePanelSampler, uv - float2(0.0, px.y)).rgb;
            float3 sharp = saturate(original + (original - neighbors * 0.25) * Sharpness * 0.5);
            float luma = dot(sharp, float3(0.2126, 0.7152, 0.0722));
            float3 colored = saturate(lerp(float3(luma, luma, luma), sharp, Saturation));
            float3 curved = colored * colored * (3.0 - 2.0 * colored);
            colored = lerp(colored, curved, Contrast * 0.65);
            if (Bloom > 0.0)
            {
                float2 radius = px * 10.0;
                float3 glow = GamePanelHighlights(uv) * 0.20;
                glow += GamePanelHighlights(uv + float2(radius.x, 0.0)) * 0.10;
                glow += GamePanelHighlights(uv - float2(radius.x, 0.0)) * 0.10;
                glow += GamePanelHighlights(uv + float2(0.0, radius.y)) * 0.10;
                glow += GamePanelHighlights(uv - float2(0.0, radius.y)) * 0.10;
                glow += GamePanelHighlights(uv + radius) * 0.10;
                glow += GamePanelHighlights(uv - radius) * 0.10;
                glow += GamePanelHighlights(uv + float2(radius.x, -radius.y)) * 0.10;
                glow += GamePanelHighlights(uv + float2(-radius.x, radius.y)) * 0.10;
                colored = 1.0 - (1.0 - colored) * (1.0 - glow * Bloom * 0.35);
            }
            float2 edge = uv * 2.0 - 1.0;
            float vignette = smoothstep(0.20, 1.60, dot(edge, edge));
            colored *= 1.0 - vignette * Vignette * 0.55;
            return float4(lerp(original, colored, Strength), 1.0);
        }
        technique GamePanelSDR
        {
            pass { VertexShader = GamePanelVS; PixelShader = GamePanelPS; }
        }
        """;
}
