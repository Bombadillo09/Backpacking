// Lettering on signs (TextMesh): the font's glyphs in the text colour, hidden behind walls and only drawn from the
// front. The built-in font material ignores depth, so signs showed through buildings, mirrored from behind.
// Lit by nothing but a touch of the scene's ambient light, like painted lettering in shade.
Shader "Backpacking/World Text"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Colour", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
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
                float fog : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // Font textures keep the glyph shapes in alpha.
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * input.color.a;
                half3 colour = input.color.rgb * saturate(0.55 + 0.9 * dot(unity_AmbientSky.rgb, half3(0.2126, 0.7152, 0.0722)));
                colour = MixFog(colour, input.fog);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
