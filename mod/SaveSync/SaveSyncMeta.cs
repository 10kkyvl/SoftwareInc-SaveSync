using UnityEngine;
using UnityEngine.UI;

namespace SaveSync
{
    public class SaveSyncMeta : ModMeta
    {
        public override string Name
        {
            get { return "Save Sync"; }
        }

        public override void ConstructOptionsScreen(RectTransform parent, bool inGame)
        {
            Text text = WindowManager.SpawnLabel();
            text.text = Texts.L(
                "Adds new content from data mods to an existing save.\nYour original save file is never changed: the result goes to a new file.",
                "Добавляет новое содержимое дата-модов в идущий сейв.\nИсходный сейв не меняется: результат идёт в новый файл.");
            WindowManager.AddElementToElement(text.gameObject, parent.gameObject, new Rect(0f, 0f, 400f, 128f), new Rect(0f, 0f, 0f, 0f));

            if (inGame)
            {
                Button button = WindowManager.SpawnButton();
                Text caption = button.GetComponentInChildren<Text>();
                if (caption != null)
                {
                    caption.text = Texts.L("Check all installed mods", "Проверить все установленные моды");
                }
                button.onClick.AddListener(OnCheckAllMods);
                WindowManager.AddElementToElement(button.gameObject, parent.gameObject, new Rect(0f, 132f, 320f, 28f), new Rect(0f, 0f, 0f, 0f));
            }
        }

        private void OnCheckAllMods()
        {
            for (int i = 0; i < ModBehaviours.Count; i++)
            {
                SaveSyncBehaviour behaviour = ModBehaviours[i] as SaveSyncBehaviour;
                if (behaviour != null)
                {
                    behaviour.CheckAllMods();
                }
            }
        }
    }
}
