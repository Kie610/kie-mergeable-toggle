// 隠れたマテリアルスロットへ差し替える「描画されない」シェーダ。
// Built-in RP が描画しない LightMode だけを持つので、このマテリアルが付いたサブメッシュは
// draw call も頂点シェーディングも出ない (ShadowCaster も無い)。
// Safety でブロックされた場合はフォールバックシェーダで描かれるが、頂点は infinimation で
// 遠方にあるので見た目は壊れず、draw call が戻るだけ。
Shader "Hidden/kieMergeableToggle/Empty"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "MT_Never" }
            ColorMask 0
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 v : POSITION) : SV_POSITION { return float4(0, 0, 0, 1); }
            fixed4 frag() : SV_Target { return 0; }
            ENDCG
        }
    }
    Fallback Off
}
