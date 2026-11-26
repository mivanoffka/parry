Shader "Parry/Smoke"
{
    Properties
    {
        _MainTex("Glow", 2D) = "white" {}
        _Intensity("HDR Intensity", Float) = 8
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                // Soft overlapping lobes give each rotated, stretched puff an uneven outline.
                float angle = atan2(p.y, p.x);
                float edge = .83 + .10 * sin(angle * 3 + .6) + .06 * sin(angle * 5 - 1.2);
                float billows = sin(p.x * 8 + p.y * 3) * sin(p.y * 7 - p.x * 2);
                half glow = pow(saturate(1 - length(p) / edge), 1.15);
                glow *= .88 + .12 * billows;
                return half4(input.color.rgb, input.color.a * glow);
            }
            ENDHLSL
        }
    }
}
