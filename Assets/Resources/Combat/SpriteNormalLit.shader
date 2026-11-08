Shader "Parry/Sprite Normal Lit"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _NormalMap("RGB Normal Map (Linear)", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0, 2)) = 1
        _Ambient("Ambient Fill", Range(0, 1)) = 0.28
        _Specular("Specular Strength", Range(0, 2)) = 0.18
        _Gloss("Specular Exponent", Range(8, 128)) = 36
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="False" "DisableBatching"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Pass
        {
            Name "SpriteLighting3D"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _NormalStrength, _Ambient, _Specular, _Gloss;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 rightWS : TEXCOORD2;
                half3 upWS : TEXCOORD3;
                half3 frontWS : TEXCOORD4;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SetUpSpriteInstanceProperties();
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 position = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                output.positionWS = TransformObjectToWorld(position);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color * _Color * unity_SpriteColor;
                // Sprite planes face local -Z. Flip the normal-map axes along with the artwork.
                output.rightWS = TransformObjectToWorldDir(float3(unity_SpriteProps.x, 0, 0));
                output.upWS = TransformObjectToWorldDir(float3(0, unity_SpriteProps.y, 0));
                output.frontWS = TransformObjectToWorldDir(float3(0, 0, -1));
                return output;
            }
            half3 ShadeLight(Light light, half3 normalWS, half3 viewWS, half3 albedo)
            {
                half diffuse = saturate(dot(normalWS, light.direction));
                half3 halfway = SafeNormalize(light.direction + viewWS);
                half specular = pow(saturate(dot(normalWS, halfway)), _Gloss) * _Specular * diffuse;
                return (albedo * diffuse + specular) * light.color * light.distanceAttenuation * light.shadowAttenuation;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                clip(color.a - 0.01);
                // Maps are uncompressed, linear RGB, not Unity's platform-specific packed normal format.
                half3 normalTS = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv).rgb * 2 - 1;
                normalTS.xy *= _NormalStrength;
                normalTS = normalize(normalTS);
                half3 normalWS = normalize(input.rightWS * normalTS.x + input.upWS * normalTS.y + input.frontWS * normalTS.z);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 result = color.rgb * max(SampleSH(normalWS), half3(_Ambient, _Ambient, _Ambient));
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                result += ShadeLight(mainLight, normalWS, viewWS, color.rgb);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX) || USE_CLUSTER_LIGHT_LOOP
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    #if USE_CLUSTER_LIGHT_LOOP
                        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        {
                            Light extra = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                            result += ShadeLight(extra, normalWS, viewWS, color.rgb);
                        }
                    #endif
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light extra = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                        result += ShadeLight(extra, normalWS, viewWS, color.rgb);
                    LIGHT_LOOP_END
                #endif
                return half4(result, color.a);
            }
            ENDHLSL
        }
    }
}
