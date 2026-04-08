Shader "Project/Particles/Match3BurstParticle"
{
    Properties
    {
        _Tint("Tint", Color) = (1,1,1,1)
        _Intensity("Intensity", Range(0, 8)) = 1
        _Softness("Softness", Range(0.001, 1)) = 0.12
        _Cutoff("Cutoff", Range(0, 1)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Intensity;
                half _Softness;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;      // Particle vertex stream: Color
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Procedural soft disc based on quad UV (no texture needed).
                float2 d = IN.uv * 2.0 - 1.0;
                float r = length(d);
                // Alpha: 1 at center, falls off near edge. Softness controls edge width.
                float edge0 = 1.0 - max(0.0001, _Softness);
                float a = 1.0 - smoothstep(edge0, 1.0, r);
                a = max(0.0, a - _Cutoff);

                half4 c = IN.color * _Tint;
                c.rgb *= _Intensity;
                c.a *= (half)a;
                return c;
            }
            ENDHLSL
        }
    }
}

