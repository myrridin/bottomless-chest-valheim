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
        /// <summary>How far above the chest title the search box sits.</summary>
        private const float SearchBoxLift = 6f;

        /// <summary>Gap between the search box and the count line beneath it.</summary>
        private const float StatusGap = 26f;

        private static GameObject _field;
        private static GameObject _hiddenTitle;

        /// <summary>
        /// Frame on which Escape was last seen.
        /// </summary>
        /// <remarks>
        /// Vanilla closes from its own Update via ZInput, which does not necessarily report
        /// the key on the same frame Unity does. Checking only the current frame therefore
        /// missed it intermittently.
        /// </remarks>
        private static int _escapeFrame = -1000;

        private const int EscapeGraceFrames = 3;

        /// <summary>Set when a close was cancelled, so the teardown postfix stands down.</summary>
        private static bool _hideCancelled;

        internal static void NoteEscapePressed() => _escapeFrame = Time.frameCount;
        private static InputField _input;
        private static Text _status;

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class ShowPatch
        {
            private static void Postfix(InventoryGui __instance, Container container)
            {
                if (container == null || !BottomlessContainer.TryResolve(container, out var bottomless))
                {
                    return;
                }

                // Always re-ask the server on open. Fetching once per session leaves this
                // client showing a stale copy of a chest anyone else has touched.
                bottomless.RefreshFromServer();

                // Order matters: Teardown clears filter state, so it has to happen before
                // Begin, never after.
                Teardown();
                ChestView.Begin(container.GetInventory(), bottomless);
                Build(__instance);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        private static class HidePatch
        {
            private static void Postfix()
            {
                // Harmony runs postfixes even when a prefix cancels the original, so without
                // this the search box was torn down while the window stayed open.
                if (_hideCancelled)
                {
                    _hideCancelled = false;
                    return;
                }

                Teardown();
                BottomlessContainer.FlushAllPending();
            }
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
                    // Sit above the title's slot and let the count line take the slot
                    // itself, so neither crowds the first row of items.
                    rect.anchoredPosition = anchorSource.anchoredPosition + new Vector2(0f, SearchBoxLift);
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
                _field.AddComponent<SearchFocusGuard>().TakeFocus();
                _field.AddComponent<ChestScroller>();
                _field.AddComponent<ChestScrollbarBridge>().Bind(gui.m_containerGrid);

                // Drawn above the panel it belongs to, so it can end up underneath a
                // neighbouring panel's raycast target - visible, but never clickable.
                _field.transform.SetAsLastSibling();

                Plugin.Log.LogDebug(
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
                statusRect.anchoredPosition = fieldRect.anchoredPosition - new Vector2(0f, StatusGap);

                UpdateStatus();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Could not build the chest search box: {ex}");
                DestroyWidgets();
            }
        }

        /// <summary>Keeps the count line current while contents are still arriving.</summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        private static class StatusRefreshPatch
        {
            private static void Postfix()
            {
                if (_status != null)
                {
                    UpdateStatus();
                }
            }
        }

        /// <summary>
        /// Handles Escape when there is a search to clear, so the window stays open.
        /// </summary>
        /// <remarks>
        /// Called from a prefix on InventoryGui.Hide rather than from an Update, because
        /// vanilla closes on ZInput Escape from its own Update and the two orderings race:
        /// Unity deactivates the field on Escape, the focus guard then unblocks input, and
        /// whether the game sees the key depends on which Update ran first. Intercepting
        /// the close itself has no such ambiguity.
        /// </remarks>
        internal static bool TryConsumeEscape()
        {
            if (_input == null || string.IsNullOrEmpty(_input.text))
            {
                return false;
            }

            var pressedNow = Input.GetKeyDown(KeyCode.Escape);
            var pressedRecently = Time.frameCount - _escapeFrame <= EscapeGraceFrames;

            if (!pressedNow && !pressedRecently)
            {
                return false;
            }

            _escapeFrame = -1000;

            _input.text = string.Empty;

            // Unity deactivated the field when it saw Escape; take focus straight back so
            // the next keystroke still goes to the search.
            _input.ActivateInputField();
            _input.Select();

            return true;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        private static class EscapeGuard
        {
            private static bool Prefix()
            {
                _hideCancelled = TryConsumeEscape();
                return !_hideCancelled;
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

            if (ChestView.AwaitingContents)
            {
                // An empty grid would otherwise read as an empty chest.
                _status.text = "Loading from server...";
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
