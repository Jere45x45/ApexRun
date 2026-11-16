// Piso de la pista que se moja con la lluvia (pipeline Built-in).
// Seco se ve igual que el Standard (mismo color, textura, normal y brillo).
// Con TrackConditions.Wetness (global _ApexWetness):
// - se oscurece y brilla,
// - aparecen charcos (ruido en el mundo, no depende de las UV),
// - con lluvia, los charcos tienen ondas,
// - si WetTrackVisuals arma el reflejo plano, refleja karts, barreras y cielo.
// WetTrackVisuals pone este shader solo en copias de los materiales del piso.
Shader "ApexRun/WetGround"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0

        _WetDarken ("Mojado: cuanto oscurece", Range(0,1)) = 0.45
        _WetSmoothness ("Mojado: brillo", Range(0,1)) = 0.75
        _PuddleAmount ("Charcos: cantidad", Range(0,1)) = 1
        _PuddleScale ("Charcos: tamano (1/m)", Float) = 0.14
        _FilmReflection ("Mojado sin charco: fuerza del reflejo", Range(0,1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf WetStandard fullforwardshadows addshadow exclude_path:deferred exclude_path:prepass
        #pragma target 3.0
        #include "UnityPBSLighting.cginc"

        sampler2D _MainTex;
        sampler2D _BumpMap;
        half4 _Color;
        half _BumpScale;
        half _Glossiness;
        half _Metallic;
        half _WetDarken;
        half _WetSmoothness;
        half _PuddleAmount;
        float _PuddleScale;
        half _FilmReflection;

        // Globales (WetTrackVisuals).
        float _ApexWetness;
        float _ApexRain;
        float _ApexPlanarOn;
        sampler2D _ApexPlanarReflection;

        // Pisadas en los charcos (WetPuddles): xy = posición XZ, z = cuándo (_ApexTime), w = fuerza.
        #define APEX_SPLASHES 32
        float4 _ApexSplashes[APEX_SPLASHES];
        float _ApexTime;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float4 screenPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        struct SurfaceOutputWet
        {
            fixed3 Albedo;
            float3 Normal;
            half3 Emission;
            half Metallic;
            half Smoothness;
            half Occlusion;
            fixed Alpha;
            float2 ScreenUV;
            half PlanarWeight;
            half PlanarScale;
        };

        inline SurfaceOutputStandard ToStandard(SurfaceOutputWet s)
        {
            SurfaceOutputStandard o;
            o.Albedo = s.Albedo;
            o.Normal = s.Normal;
            o.Emission = s.Emission;
            o.Metallic = s.Metallic;
            o.Smoothness = s.Smoothness;
            o.Occlusion = s.Occlusion;
            o.Alpha = s.Alpha;
            return o;
        }

        inline half4 LightingWetStandard(SurfaceOutputWet s, float3 viewDir, UnityGI gi)
        {
            return LightingStandard(ToStandard(s), viewDir, gi);
        }

        inline void LightingWetStandard_GI(SurfaceOutputWet s, UnityGIInput data, inout UnityGI gi)
        {
            SurfaceOutputStandard o = ToStandard(s);
            LightingStandard_GI(o, data, gi);

            if (s.PlanarWeight > 0.001)
            {
                // Más áspero = más borroso (mips del reflejo).
                half mip = min((1.0 - s.Smoothness) * 14.0, 7.0);
                half3 planar = tex2Dlod(_ApexPlanarReflection, float4(s.ScreenUV, 0, mip)).rgb;
                gi.indirect.specular = lerp(gi.indirect.specular, planar * s.PlanarScale, s.PlanarWeight);
            }
        }

        // ---------- Ruido ----------

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float2 Hash22(float2 p)
        {
            float n = Hash21(p);
            return float2(n, Hash21(p + n + 19.19));
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i);
            float b = Hash21(i + float2(1, 0));
            float c = Hash21(i + float2(0, 1));
            float d = Hash21(i + float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        float PuddleNoise(float2 p)
        {
            return ValueNoise(p) * 0.6 + ValueNoise(p * 2.7 + 5.1) * 0.3 + ValueNoise(p * 6.1 + 1.7) * 0.1;
        }

        // Una onda por celda: un anillo que crece y se apaga. Devuelve la pendiente en XZ.
        float2 Ripple(float2 p, float t)
        {
            float2 cell = floor(p);
            float2 f = frac(p);
            float2 h = Hash22(cell);
            float2 center = 0.25 + 0.5 * h;
            float phase = frac(t * 0.9 + h.x * 7.3);
            float2 d = f - center;
            float dist = length(d);
            float x = (dist - phase * 0.4) * 22.0;
            float wave = sin(x * 3.14159) * saturate(1.0 - abs(x)) * (1.0 - phase);
            return dist > 0.0001 ? d / dist * wave : float2(0, 0);
        }

        // Anillos que se abren donde una rueda cruzó el charco. Devuelve la pendiente en XZ.
        float2 SplashRings(float2 p)
        {
            float2 g = float2(0, 0);

            [loop]
            for (int i = 0; i < APEX_SPLASHES; i++)
            {
                float4 s = _ApexSplashes[i];
                float age = _ApexTime - s.z;

                if (s.w <= 0.0 || age < 0.0 || age > 1.6)
                    continue;

                float2 d = p - s.xy;
                float dist = length(d);
                float radius = 0.15 + age * 1.6;

                if (dist > radius + 0.4 || dist < 0.0001)
                    continue;

                float x = (dist - radius) * 9.0;
                float fade = 1.0 - age / 1.6;
                float wave = sin(x * 3.14159) * saturate(1.0 - abs(x) / 2.5) * fade * fade * s.w;
                g += d / dist * wave;
            }

            return g;
        }

        void surf(Input IN, inout SurfaceOutputWet o)
        {
            half4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            half3 n = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);

            o.Metallic = _Metallic;
            o.Occlusion = 1;
            o.Alpha = c.a;
            o.Emission = 0;
            o.ScreenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);
            o.PlanarWeight = 0;
            o.PlanarScale = 1;

            float wet = saturate(_ApexWetness);

            if (wet <= 0.001)
            {
                o.Albedo = c.rgb;
                o.Normal = n;
                o.Smoothness = _Glossiness;
                return;
            }

            float3 worldUp = WorldNormalVector(IN, float3(0, 0, 1));
            half up = saturate((worldUp.y - 0.6) / 0.3);

            // Película de agua: se ve mojado enseguida (los charcos tardan más). Más en lo plano.
            half film = saturate(wet * 1.7) * lerp(0.5, 1.0, up);

            // Charcos: empiezan a aparecer con 25 % de humedad y crecen hasta empapada.
            half grow = saturate((wet - 0.25) / 0.75) * _PuddleAmount;
            half threshold = lerp(1.05, 0.61, grow);
            half noise = PuddleNoise(IN.worldPos.xz * _PuddleScale);
            half puddle = smoothstep(threshold, threshold + 0.07, noise) * up * step(0.001, grow);

            half3 albedo = c.rgb * lerp(1.0, 1.0 - _WetDarken, film);
            albedo *= lerp(1.0, 0.6, puddle);

            half smooth = lerp(_Glossiness, max(_Glossiness, _WetSmoothness), film);
            smooth = lerp(smooth, 0.96, puddle);

            // El agua alisa el relieve; en los charcos, el piso queda plano.
            n = normalize(lerp(n, half3(0, 0, 1), saturate(film * 0.5 + puddle)));

            // Ondas en los charcos: las de la lluvia y las que dejan las ruedas al cruzarlos.
            if (puddle > 0.01)
            {
                float2 g = float2(0, 0);

                if (_ApexRain > 0.01)
                    g += (Ripple(IN.worldPos.xz * 2.3, _Time.y) + Ripple(IN.worldPos.xz * 3.1 + 17.0, _Time.y * 1.2 + 0.5)) * 0.35 * saturate(_ApexRain);

                g += SplashRings(IN.worldPos.xz) * 0.6;
                g *= puddle;
                float3 tWorld = WorldNormalVector(IN, float3(1, 0, 0));
                float3 bWorld = WorldNormalVector(IN, float3(0, 1, 0));
                float3 gWorld = float3(g.x, 0, g.y);
                n = normalize(n + half3(dot(gWorld, tWorld), dot(gWorld, bWorld), 0));
            }

            o.Albedo = albedo;
            o.Normal = n;
            o.Smoothness = smooth;
            o.ScreenUV += n.xy * 0.02 * puddle;
            // Lo mojado refleja la escena de verdad (no el cielo de día por defecto), más tenue fuera de los charcos.
            o.PlanarWeight = _ApexPlanarOn * up * saturate(max(film, puddle));
            o.PlanarScale = lerp(_FilmReflection, 1.0, puddle);
        }
        ENDCG
    }

    FallBack "Standard"
}
