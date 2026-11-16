// Alambrado de la pista (Built-in, forward).
//
// - Las dos caras se pintan (Cull Off) y quedan iguales: la luz del sol y de
//   los focos no depende de hacia dónde mira la cara, solo de cuánta luz llega
//   y de si está a la sombra. Así un lado no queda claro y el otro oscuro.
// - Sin brillo ni reflejos.
// - Los agujeros se recortan con el alfa de la textura (no es semitransparente),
//   así no hay problemas de orden entre rejas y proyecta la sombra del alambre.
Shader "ApexRun/Reja"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Textura (alfa = alambre)", 2D) = "white" {}
        _Cutoff ("Recorte del alfa", Range(0.01, 1)) = 0.5
        _LightAmount ("Luz directa (sol y focos)", Range(0, 1.5)) = 0.7
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Reja alphatest:_Cutoff addshadow fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _LightAmount;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }

        // Luz directa sin el término N·L: misma iluminación en las dos caras.
        // atten trae la distancia (focos) y la sombra.
        half4 LightingReja (SurfaceOutput s, half3 lightDir, half atten)
        {
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (atten * _LightAmount);
            c.a = s.Alpha;
            return c;
        }
        ENDCG
    }

    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
