Shader "Ilumisoft/PostProcessing/Blur"
{
    HLSLINCLUDE
    
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _Lerp;
        TEXTURE2D(_BlurTexA);
        TEXTURE2D(_BlurTexB);

        float4 Downsample (Varyings input) : SV_Target
        {
            float3 color = 0;
            float4 offset = _BlitTexture_TexelSize.xyxy*float4(-1,-1,1,1);

            color += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb * 4;
            color += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord + offset.xy).rgb;
            color += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord + offset.xw).rgb;
            color += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord + offset.zy).rgb;
            color += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord + offset.zw).rgb;

            return float4(color/8, 1);
        }

        float4 Upsample (Varyings input) : SV_Target
        {
            float3 color = 0;
            float4 offset = _BlitTexture_TexelSize.xyxy*float4(-1,-1,1,1);
            
            color += SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, input.texcoord + float2(offset.x, 0)).rgb;
            color += SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, input.texcoord + float2(offset.z, 0)).rgb;
            color += SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, input.texcoord + float2(0, offset.y)).rgb;
            color += SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, input.texcoord + float2(0, offset.w)).rgb;
            color += SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, input.texcoord + offset.xy / 2.0).rgb * 2;
            color += SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, input.texcoord + offset.xw / 2.0).rgb * 2;
            color += SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, input.texcoord + offset.zy / 2.0).rgb * 2;
            color += SAMPLE_TEXTURE2D(_BlitTexture,sampler_LinearClamp, input.texcoord + offset.zw / 2.0).rgb * 2;

            return float4(color / 12, 1);
        }

        float4 BlendBlur(Varyings input) : SV_Target
        {
            float4 colA = SAMPLE_TEXTURE2D(_BlurTexA, sampler_LinearClamp, input.texcoord);
            float4 colB = SAMPLE_TEXTURE2D(_BlurTexB, sampler_LinearClamp, input.texcoord);
            return lerp(colA, colB, _Lerp);
        }
    
    ENDHLSL
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZWrite Off Cull Off
        Pass
        {
            Name "DownsamplePass"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Downsample
            
            ENDHLSL
        }
        
        Pass
        {
            Name "UpsamplePass"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Upsample
            
            ENDHLSL
        }

        Pass
        {
            Name "BlendBlurPass"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlendBlur
            ENDHLSL
        }
    }
}
