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
            => Apply(profile, camera, sun, nightAmount, DayNightCycle.Sun(null));

        /// <summary>
        /// Aplica o clima com o sol posicionado pelo arco do dia.
        ///
        /// A ordem das misturas importa: o alaranjado entra na cor DE DIA e só depois o resultado
        /// é interpolado para a noite. Assim o poente escurece PARA a noite em vez de disputar com
        /// ela — se o laranja fosse aplicado por último, ele reacenderia o céu já escuro.
        /// </summary>
        public static void Apply(AtmosphereProfile profile, Camera camera, Light sun, float nightAmount,
                                 DayNightCycle.SunOrientation orientation)
        {
            if (profile == null) return;

            float n = Mathf.Clamp01(nightAmount);
            float horizon = Mathf.Clamp01(orientation.Horizon01);

            if (sun != null)
            {
                var daySunColor = Color.Lerp(profile.DaySunColor, profile.HorizonSunColor, horizon);
                sun.color = Color.Lerp(daySunColor, profile.SunColor, n);

                // Sol rente atravessa mais ar e chega mais fraco. Sem essa queda o meio-dia e o
                // amanhecer teriam a mesma força e só mudariam de cor, que lê como filtro.
                float dayIntensity = profile.DaySunIntensity * Mathf.Lerp(1f, 0.72f, horizon);
                sun.intensity = Mathf.Lerp(dayIntensity, profile.SunIntensity, n);

                // NASCE de um lado e SE PÕE do outro, varrendo o céu ao longo dos 5 minutos. A
                // elevação e o azimute vêm do relógio da fase; o Y do perfil diz só onde fica o
                // meio-dia. Sombras que giram e se alongam são a leitura mais antiga que existe de
                // "está ficando tarde", e não ocupam HUD nenhum.
                sun.transform.rotation = Quaternion.Euler(orientation.Elevation,
                                                          profile.SunAngles.y + orientation.AzimuthFromNoon,
                                                          profile.SunAngles.z);

                sun.shadows = profile.SunShadows ? LightShadows.Soft : LightShadows.None;
                sun.shadowStrength = Mathf.Lerp(profile.DayShadowStrength, profile.ShadowStrength, n);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(profile.DayAmbientColor, profile.AmbientColor, n);

            // A névoa esquenta menos que o sol (0,75): ar totalmente laranja engoliria a silhueta
            // das peças, e silhueta é a gramática de leitura do jogo inteiro.
            var dayFogColor = Color.Lerp(profile.DayFogColor, profile.HorizonFogColor, horizon * 0.75f);
            var fogColor = Color.Lerp(dayFogColor, profile.FogColor, n);

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
