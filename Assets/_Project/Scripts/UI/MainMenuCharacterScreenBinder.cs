using Project.Character.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Вешается на любой объект в главном меню: открывает экран персонажа по клику на BottomHeaderLogo,
    /// скрывает отдельную кнопку OpenCharacterButton в CharacterHudOverlay.
    /// </summary>
    public sealed class MainMenuCharacterScreenBinder : MonoBehaviour
    {
        [SerializeField] private CharacterScreenController characterScreen;
        [SerializeField] private string hudRootName = "MainMenuHudOverlay";
        [SerializeField] private string bottomLogoName = "BottomHeaderLogo";

        private void Start()
        {
            if (characterScreen == null)
                characterScreen = FindObjectOfType<CharacterScreenController>(true);

            if (characterScreen == null) return;

            characterScreen.HideOpenButton();

            var hud = string.IsNullOrEmpty(hudRootName) ? null : GameObject.Find(hudRootName);
            if (hud == null) return;

            var logo = FindDeepChild(hud.transform, bottomLogoName);
            if (logo == null) return;

            var btn = logo.GetComponent<Button>();
            if (btn == null) btn = logo.gameObject.AddComponent<Button>();
            var g = logo.GetComponent<Graphic>();
            if (g != null) btn.targetGraphic = g;
            btn.onClick.RemoveListener(OnOpenCharacterClicked);
            btn.onClick.AddListener(OnOpenCharacterClicked);
        }

        private void OnOpenCharacterClicked() => characterScreen?.Open();

        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var f = FindDeepChild(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
