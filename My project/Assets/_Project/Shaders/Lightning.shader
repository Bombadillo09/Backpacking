// A lightning bolt: glowing, added on top of whatever is behind it, and not dimmed by fog (fog fading is done by
// the storm, which knows the distance). Vertex colour is the tint; its alpha, times _Intensity, the brightness.
Shader "Backpacking/Lightning"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // Bright core, soft glowing edges across the width of the line.
                float across = abs(input.uv.y * 2.0 - 1.0);
                float glow = saturate(1.0 - across);
                glow = glow * glow + smoothstep(0.35, 0.0, across) * 2.0;
                return half4(input.color.rgb * input.color.a * _Intensity * glow, 0);
            }
            ENDHLSL
        }
    }
}
