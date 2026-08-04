using DestinyTogether.Data;
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
        public static void Apply(AtmosphereProfile profile, Camera camera, Light sun)
        {
            if (profile == null) return;

            if (sun != null)
            {
                sun.color = profile.SunColor;
                sun.intensity = profile.SunIntensity;
                sun.transform.rotation = Quaternion.Euler(profile.SunAngles);
                sun.shadows = profile.SunShadows ? LightShadows.Soft : LightShadows.None;
                sun.shadowStrength = profile.ShadowStrength;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = profile.AmbientColor;

            RenderSettings.fog = profile.FogEnabled;
            RenderSettings.fogColor = profile.FogColor;
            RenderSettings.fogMode = profile.FogMode;
            RenderSettings.fogDensity = profile.FogDensity;
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
                camera.backgroundColor = profile.FogEnabled ? profile.FogColor : profile.BackgroundColor;
            }
        }
    }
}
