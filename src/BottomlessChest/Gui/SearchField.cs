using System;
using GUIFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// The search box's text field, over either of the two input widgets Valheim uses.
    /// </summary>
    /// <remarks>
    /// Valheim 1.0's build panel gained a search field, and it is a <c>GuiInputField</c> -
    /// a <c>TMP_InputField</c> subclass. That matters for one reason: TMP raises
    /// <c>onSelect</c> and <c>onDeselect</c>, so focus can be observed rather than polled
    /// every frame, which is what this mod had to do with the legacy
    /// <c>UnityEngine.UI.InputField</c>. It also brings the on-screen keyboard 1.0 needs for
    /// its console builds, and gamepad caret handling.
    ///
    /// The field is cloned from the build panel's rather than assembled here. A TMP input
    /// needs a text component, a placeholder, a viewport and a caret wired together, and
    /// copying the one the game already ships gets all of that plus its exact appearance.
    ///
    /// If that clone cannot be made - the build UI is not loaded, or a future version moves
    /// it - this falls back to the legacy field the mod has always used. Construction
    /// differs; behaviour past that point does not, which is the point of this class.
    /// </remarks>
    internal sealed class SearchField
    {
        private readonly GuiInputField _modern;
        private readonly InputField _legacy;

        private SearchField(GameObject gameObject, GuiInputField modern, InputField legacy)
        {
            GameObject = gameObject;
            _modern = modern;
            _legacy = legacy;
        }

        internal GameObject GameObject { get; }

        /// <summary>
        /// True when focus changes arrive as events rather than needing to be polled.
        /// </summary>
        internal bool ReportsFocusChanges => _modern != null;

        internal string Text
        {
            get => _modern != null ? _modern.text : _legacy.text;
            set
            {
                if (_modern != null)
                {
                    _modern.text = value;
                }
                else
                {
                    _legacy.text = value;
                }
            }
        }

        internal bool IsFocused => _modern != null ? _modern.isFocused : _legacy.isFocused;

        internal void OnChanged(Action<string> handler)
        {
            if (_modern != null)
            {
                _modern.onValueChanged.AddListener(text => handler(text));
            }
            else
            {
                _legacy.onValueChanged.AddListener(text => handler(text));
            }
        }

        /// <summary>Subscribes to focus events. Only call when <see cref="ReportsFocusChanges"/>.</summary>
        internal void OnFocusChanged(Action gained, Action lost)
        {
            _modern.onSelect.AddListener(_ => gained());
            _modern.onDeselect.AddListener(_ => lost());
        }

        internal void Focus()
        {
            if (_modern != null)
            {
                _modern.Select();
                _modern.ActivateInputField();
            }
            else
            {
                _legacy.Select();
                _legacy.ActivateInputField();
            }
        }

        /// <summary>
        /// Clones the build panel's search field, or null if it cannot be reached.
        /// </summary>
        /// <remarks>
        /// Runtime <c>AddListener</c> subscriptions are not serialized, so the clone does not
        /// carry the build UI's handlers with it. Everything visual does come along.
        /// </remarks>
        internal static SearchField TryCloneVanilla(Transform parent)
        {
            try
            {
                var source = Hud.instance != null && Hud.instance.m_buildUi != null
                    ? Hud.instance.m_buildUi.m_searchField
                    : null;

                if (source == null)
                {
                    return null;
                }

                var clone = UnityEngine.Object.Instantiate(source.gameObject, parent);
                clone.name = "BottomlessChestSearch";
                clone.SetActive(true);

                var field = clone.GetComponent<GuiInputField>();
                if (field == null)
                {
                    UnityEngine.Object.Destroy(clone);
                    return null;
                }

                // Listeners first: clearing the text fires onValueChanged, and any handler
                // the clone inherited would be talking to the build panel about it.
                field.onValueChanged.RemoveAllListeners();
                field.onSelect.RemoveAllListeners();
                field.onDeselect.RemoveAllListeners();
                field.text = string.Empty;

                // The build panel wires its field into a grid of piece buttons. Ours has
                // nowhere to navigate to, and leaving the targets in place would send a
                // gamepad off into a panel that is not open.
                var navigation = field.navigation;
                navigation.mode = UnityEngine.UI.Navigation.Mode.None;
                field.navigation = navigation;

                SetPlaceholder(field, "$bottomless_search");

                return new SearchField(clone, field, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    $"Could not clone the build panel's search field, falling back to the " +
                    $"legacy one: {ex.Message}");

                return null;
            }
        }

        /// <summary>Replaces the inherited placeholder, which advertises the build panel.</summary>
        private static void SetPlaceholder(TMP_InputField field, string text)
        {
            var placeholder = field.placeholder as TMP_Text;
            if (placeholder == null)
            {
                return;
            }

            placeholder.text = Localization.instance != null
                ? Localization.instance.Localize(text)
                : text;
        }

        internal static SearchField FromLegacy(GameObject created)
        {
            var input = created.GetComponent<InputField>() ?? created.GetComponentInChildren<InputField>();
            return input == null ? null : new SearchField(created, null, input);
        }
    }
}
