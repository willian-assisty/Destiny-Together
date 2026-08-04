using DestinyTogether.Data;
using DestinyTogether.Sim;
using UnityEngine;

namespace DestinyTogether.Presentation
{
    /// <summary>
    /// Aplica um <see cref="AtmosphereProfile"/> na cena montada em runtime.
    ///
    /// Separado do asset de dados porque quem escreve em RenderSettings é apresentação, não
    /// conteúdo — a mesma razão pela qual a simulação não sabe o que é uma cor.
    /// </summary>
    public static class AtmosphereApplier
    {
        /// <summary>Aplica o clima da noite fechada. Equivale a <c>Apply(profile, cam, sun, 1f)</c>.</summary>
        public static void Apply(AtmosphereProfile profile, Camera camera, Light sun)
            => Apply(profile, camera, sun, 1f);

        /// <summary>
        /// Aplica o clima interpolado entre dia e noite.
        ///
        /// Chamado por frame durante a partida, com <paramref name="nightAmount"/> vindo de
        /// <see cref="DestinyTogether.Sim.DayNightCycle"/>. Escrever em RenderSettings todo frame
        /// e barato — sao atribuicoes de campo, nao realocacao de recurso — e e o que faz o
        /// entardecer ser continuo em vez de um corte entre dois estados.
        /// </summary>
        public static void Apply(AtmosphereProfile profile, Camera camera, Light sun, float nightAmount)
        {
            if (profile == null) return;

            float n = Mathf.Clamp01(nightAmount);

            if (sun != null)
            {
                sun.color = Color.Lerp(profile.DaySunColor, profile.SunColor, n);
                sun.intensity = Mathf.Lerp(profile.DaySunIntensity, profile.SunIntensity, n);

                // O sol DESCE ao longo do entardecer. Sombras que se alongam sao a leitura mais
                // antiga que existe de "esta ficando tarde", e ela nao ocupa HUD nenhum.
                var angles = profile.SunAngles;
                angles.x = DayNightCycle.SunPitch(n);
                sun.transform.rotation = Quaternion.Euler(angles);

                sun.shadows = profile.SunShadows ? LightShadows.Soft : LightShadows.None;
                sun.shadowStrength = Mathf.Lerp(profile.DayShadowStrength, profile.ShadowStrength, n);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(profile.DayAmbientColor, profile.AmbientColor, n);

            var fogColor = Color.Lerp(profile.DayFogColor, profile.FogColor, n);

            RenderSettings.fog = profile.FogEnabled;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = profile.FogMode;
            RenderSettings.fogDensity = Mathf.Lerp(profile.DayFogDensity, profile.FogDensity, n);
            RenderSettings.fogStartDistance = profile.FogStart;
            RenderSettings.fogEndDistance = profile.FogEnd;

            if (profile.Skybox != null)
            {
                RenderSettings.skybox = profile.Skybox;
                DynamicGI.UpdateEnvironment();
            }

            if (camera == null) return;

            if (profile.UseSkyboxAsBackground && profile.Skybox != null)
            {
                camera.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                // Fundo igual à cor da névoa: sem isso aparece uma linha dura no horizonte,
                // exatamente onde a névoa deveria estar escondendo o fim do mundo.
                camera.backgroundColor = profile.FogEnabled
                    ? fogColor
                    : Color.Lerp(profile.DayBackgroundColor, profile.BackgroundColor, n);
            }
        }
    }
}
