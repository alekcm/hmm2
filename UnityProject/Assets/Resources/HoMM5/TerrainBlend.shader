Shader "HoMM5/TerrainBlend"
{
    Properties
    {
        _Ground ("Original terrain textures", 2DArray) = "" {}
        _Coverage ("Original coverage planes", 2DArray) = "" {}
        _LayerCount ("Layers", Int) = 1
        _WeightSize ("Coverage size", Float) = 1
        _TileSize ("World units per tile", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.5
        #pragma require 2darray
        #include "UnityCG.cginc"
        UNITY_DECLARE_TEX2DARRAY(_Ground);
        UNITY_DECLARE_TEX2DARRAY(_Coverage);
        int _LayerCount;
        float _WeightSize;
        float _TileSize;
        struct Input { float3 worldPos; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 tile = IN.worldPos.xz / _TileSize;
            float2 groundUV = tile * 0.25;
            // Coverage values sit at vertex centres, not texture edges.
            float2 weightUV = (tile + 0.5) / _WeightSize;
            float3 color = UNITY_SAMPLE_TEX2DARRAY(_Ground, float3(groundUV, 0)).rgb;
            // Imported priority order: each upper paint layer covers the lower.
            for (int layer = 1; layer < _LayerCount; ++layer)
            {
                float weight = UNITY_SAMPLE_TEX2DARRAY(_Coverage, float3(weightUV, layer)).r;
                float3 next = UNITY_SAMPLE_TEX2DARRAY(_Ground, float3(groundUV, layer)).rgb;
                color = lerp(color, next, weight);
            }
            o.Albedo = color;
            o.Metallic = 0;
            o.Smoothness = 0.05;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
