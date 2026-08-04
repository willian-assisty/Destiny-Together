using UnityEngine;

namespace DestinyTogether.UI
{
    /// <summary>
    /// Estilos IMGUI compartilhados por HUD, menu e painel de teste.
    ///
    /// Toda esta camada de UI e placeholder e sera substituida junto com a arte. O que NAO e
    /// descartavel e a informacao que ela mostra — isso foi escolhido antes de existir tela.
    /// Centralizar os estilos aqui e o que impede o menu e o HUD de divergirem enquanto isso.
    /// </summary>
    public sealed class UiStyles
    {
        public static readonly Color Ink = new Color(0.88f, 0.90f, 0.94f);
        public static readonly Color Dim = new Color(0.58f, 0.62f, 0.68f);
        public static readonly Color Accent = new Color(1f, 0.84f, 0.38f);
        public static readonly Color Good = new Color(0.30f, 0.85f, 0.45f);
        public static readonly Color Bad = new Color(0.95f, 0.30f, 0.30f);

        public GUIStyle Panel, Label, Small, Title, Big, Huge, Mono, Button, ButtonOn, Center;

        private Texture2D _panelTex, _veilTex, _buttonTex, _buttonOnTex;
        private bool _built;

        public Texture2D VeilTexture => _veilTex;

        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _panelTex = Solid(new Color(0.05f, 0.06f, 0.08f, 0.88f));
            _veilTex = Solid(new Color(0.03f, 0.035f, 0.05f, 0.94f));
            _buttonTex = Solid(new Color(0.13f, 0.15f, 0.19f, 1f));
            _buttonOnTex = Solid(new Color(0.24f, 0.30f, 0.40f, 1f));

            Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 12, 12) };
            Panel.normal.background = _panelTex;

            Label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, wordWrap = true };
            Label.normal.textColor = Ink;

            Small = new GUIStyle(Label) { fontSize = 11 };
            Small.normal.textColor = Dim;

            Title = new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false };
            Big = new GUIStyle(Label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = false };
            Huge = new GUIStyle(Label) { fontSize = 46, fontStyle = FontStyle.Bold, wordWrap = false };
            Huge.normal.textColor = Accent;

            Center = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };

            var mono = Font.CreateDynamicFontFromOSFont("Consolas", 14);
            Mono = new GUIStyle(Label) { fontSize = 14, wordWrap = false };
            if (mono != null) Mono.font = mono;

            Button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                richText = true,
                padding = new RectOffset(14, 14, 9, 9)
            };
            Button.normal.background = _buttonTex;
            Button.hover.background = _buttonOnTex;
            Button.active.background = _buttonOnTex;
            Button.normal.textColor = Ink;
            Button.hover.textColor = Color.white;

            ButtonOn = new GUIStyle(Button);
            ButtonOn.normal.background = _buttonOnTex;
            ButtonOn.normal.textColor = Accent;
            ButtonOn.fontStyle = FontStyle.Bold;
        }

        /// <summary>Escurece a tela inteira. Usado por menu, pausa e resultado.</summary>
        public void DrawVeil()
        {
            if (_veilTex == null) return;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _veilTex);
        }

        /// <summary>Botao de escolha: fica destacado quando e a opcao ativa.</summary>
        public bool Choice(string label, bool selected, params GUILayoutOption[] options)
            => GUILayout.Button(label, selected ? ButtonOn : Button, options);

        private static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        public void Dispose()
        {
            foreach (var tex in new[] { _panelTex, _veilTex, _buttonTex, _buttonOnTex })
                if (tex != null) Object.Destroy(tex);
            _built = false;
        }
    }
}
