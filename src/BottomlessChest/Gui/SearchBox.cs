using BottomlessChest.Core;
using BottomlessChest.Filter;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Puts a search field above the chest grid and keeps it wired to the filter.
    /// </summary>
    internal static class SearchBox
    {
        private static GameObject _field;
        private static GameObject _hiddenTitle;
        private static InputField _input;
        private static Text _status;

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class ShowPatch
        {
            private static void Postfix(InventoryGui __instance, Container container)
            {
                if (container == null || !BottomlessContainer.TryResolve(container, out _))
                {
                    return;
                }

                // Order matters: Teardown clears filter state, so it has to happen before
                // Begin, never after.
                Teardown();
                ChestView.Begin(container.GetInventory());
                Build(__instance);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        private static class HidePatch
        {
            private static void Postfix() => Teardown();
        }

        private static void Build(InventoryGui gui)
        {
            if (gui.m_container == null)
            {
                return;
            }

            try
            {
                // Sit exactly where the chest title does. The title is always the same
                // word and the space is better spent on the thing you actually use.
                var title = gui.m_containerName;
                var anchorSource = title != null ? title.rectTransform : null;
                var parent = anchorSource != null ? anchorSource.parent : gui.m_container.transform;

                _field = GUIManager.Instance.CreateInputField(
                    parent: parent,
                    anchorMin: new Vector2(0.5f, 1f),
                    anchorMax: new Vector2(0.5f, 1f),
                    position: new Vector2(0f, 44f),
                    contentType: InputField.ContentType.Standard,
                    placeholderText: "$bottomless_search",
                    fontSize: 16,
                    width: 280f,
                    height: 30f);

                if (anchorSource != null)
                {
                    var rect = _field.GetComponent<RectTransform>();
                    rect.anchorMin = anchorSource.anchorMin;
                    rect.anchorMax = anchorSource.anchorMax;
                    rect.pivot = anchorSource.pivot;
                    rect.anchoredPosition = anchorSource.anchoredPosition;
                    rect.sizeDelta = new Vector2(280f, 30f);

                    _hiddenTitle = title.gameObject;
                    _hiddenTitle.SetActive(false);
                }

                _input = _field.GetComponent<InputField>() ?? _field.GetComponentInChildren<InputField>();
                if (_input == null)
                {
                    Plugin.Log.LogError("Search field was created but carries no InputField.");
                    DestroyWidgets();
                    return;
                }

                _input.onValueChanged.AddListener(OnChanged);
                _field.AddComponent<SearchFocusGuard>();
                _field.AddComponent<ChestScroller>();

                // Drawn above the panel it belongs to, so it can end up underneath a
                // neighbouring panel's raycast target - visible, but never clickable.
                _field.transform.SetAsLastSibling();

                // Focus it immediately: you opened a searchable chest, so typing should
                // just work without hunting for the box first.
                _input.Select();
                _input.ActivateInputField();

                Plugin.Log.LogInfo(
                    $"Search box ready (parent '{gui.m_container.name}', " +
                    $"{ChestView.TotalCount} items in chest).");

                _status = GUIManager.Instance.CreateText(
                    text: string.Empty,
                    parent: _field.transform.parent,
                    anchorMin: new Vector2(0.5f, 1f),
                    anchorMax: new Vector2(0.5f, 1f),
                    position: Vector2.zero,
                    font: GUIManager.Instance.AveriaSerifBold,
                    fontSize: 13,
                    color: GUIManager.Instance.ValheimOrange,
                    outline: true,
                    outlineColor: Color.black,
                    width: 280f,
                    height: 20f,
                    addContentSizeFitter: false).GetComponent<Text>();

                var fieldRect = _field.GetComponent<RectTransform>();
                var statusRect = _status.rectTransform;
                statusRect.anchorMin = fieldRect.anchorMin;
                statusRect.anchorMax = fieldRect.anchorMax;
                statusRect.pivot = fieldRect.pivot;
                statusRect.anchoredPosition = fieldRect.anchoredPosition - new Vector2(0f, 26f);

                UpdateStatus();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Could not build the chest search box: {ex}");
                DestroyWidgets();
            }
        }

        private static void OnChanged(string text)
        {
            try
            {
                ChestView.SetQuery(text);
                UpdateStatus();
            }
            catch (System.Exception ex)
            {
                // Unity swallows exceptions thrown from UI callbacks.
                Plugin.Log.LogError($"Filtering failed for '{text}': {ex}");
            }
        }

        private static void UpdateStatus()
        {
            if (_status == null)
            {
                return;
            }

            var shown = ChestView.IsFiltering
                ? $"{ChestView.MatchCount} of {ChestView.TotalCount} items"
                : $"{ChestView.TotalCount} items";

            var rows = ChestView.TotalRows;
            var visible = ChestView.VisibleRows;

            _status.text = rows > visible
                ? $"{shown}   -   rows {ChestView.ScrollRow + 1}-{Mathf.Min(ChestView.ScrollRow + visible, rows)} of {rows}"
                : shown;
        }

        private static void Teardown()
        {
            DestroyWidgets();
            ChestView.End();
        }

        private static void DestroyWidgets()
        {
            if (_hiddenTitle != null)
            {
                _hiddenTitle.SetActive(true);
                _hiddenTitle = null;
            }

            if (_field != null)
            {
                GUIManager.BlockInput(false);
                Object.Destroy(_field);
                _field = null;
                _input = null;
            }

            if (_status != null)
            {
                Object.Destroy(_status.gameObject);
                _status = null;
            }
        }
    }
}
