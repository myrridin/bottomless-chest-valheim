using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace BottomlessChest.Gui
{
    /// <summary>
    /// Blocks game input only while the search field actually has focus.
    /// </summary>
    /// <remarks>
    /// Without this, typing "wood" walks the player forward. Legacy InputField exposes no
    /// focus-gained event, so focus is polled instead.
    ///
    /// Deliberately scoped to the focused period rather than the whole time the chest is
    /// open: blocking input for the entire window risks swallowing the key that closes it.
    /// Lives on the field's own GameObject so it cannot outlive it.
    /// </remarks>
    /// <remarks>
    /// Runs before other behaviours so that Escape is recorded before InventoryGui.Update
    /// gets to decide whether to close. Without a fixed order the two race and Escape
    /// closes the window despite there being a search to clear.
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    internal sealed class SearchFocusGuard : MonoBehaviour
    {
        private InputField _input;
        private bool _blocking;
        private int _focusAttemptsLeft;
        private bool _wasFocused;

        private void Awake()
        {
            _input = GetComponent<InputField>();
        }

        /// <summary>
        /// Asks for keyboard focus, retrying for a few frames.
        /// </summary>
        /// <remarks>
        /// Focusing once during Show is not reliable: the inventory GUI is still being
        /// built and something else can take the selection immediately afterwards, leaving
        /// the box looking focused but ignoring keystrokes. Retrying until it sticks is
        /// cheap and removes the race.
        /// </remarks>
        internal void TakeFocus()
        {
            // Generous, because focus is deliberately withheld while the use key is still
            // held down and a hold-to-deposit gesture can last a while.
            _focusAttemptsLeft = 300;
        }

        /// <summary>
        /// Whether the key that opened the chest is still being held.
        /// </summary>
        /// <remarks>
        /// Holding the use key on a chest is the deposit gesture. Taking focus during it
        /// sends the held key into the search box - the chest opens, fills the filter with
        /// repeats of that letter, and closes again. Focus waits for the key to come up.
        /// </remarks>
        private static bool UseKeyHeld()
        {
            try
            {
                if (ZInput.instance != null && ZInput.GetButton("Use"))
                {
                    return true;
                }
            }
            catch
            {
                // ZInput is not available in every context; fall through to the raw key.
            }

            return Input.GetKey(KeyCode.E);
        }

        private void Update()
        {
            if (_input == null)
            {
                Release();
                return;
            }

            if (_focusAttemptsLeft > 0)
            {
                _focusAttemptsLeft--;

                if (UseKeyHeld())
                {
                    // Do not take focus mid-gesture.
                }
                else if (!_input.isFocused)
                {
                    _input.Select();
                    _input.ActivateInputField();
                }
                else
                {
                    _focusAttemptsLeft = 0;
                }
            }

            // Unity's InputField handles Escape in its own update and deactivates itself,
            // so by the time this runs focus is already gone. Remembering last frame is what
            // makes the first press count.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SearchBox.NoteEscapePressed();
            }

            if ((_input.isFocused || _wasFocused) && Input.GetKeyDown(KeyCode.Escape))
            {
                _wasFocused = false;
                HandleEscape();
                return;
            }

            _wasFocused = _input.isFocused;

            if (_input.isFocused == _blocking)
            {
                return;
            }

            _blocking = _input.isFocused;
            GUIManager.BlockInput(_blocking);
            Plugin.Log.LogDebug($"Search box focus: {_blocking}");
        }

        /// <summary>
        /// Escape clears the search if there is one, and otherwise closes the chest.
        /// </summary>
        /// <remarks>
        /// Blocking game input while the box has focus also stops Escape reaching the game,
        /// so without this it takes two presses to leave: one to drop focus and one to
        /// actually close. Handling it here makes the first press do what was meant.
        /// </remarks>
        private void HandleEscape()
        {
            if (!string.IsNullOrEmpty(_input.text))
            {
                _input.text = string.Empty;
                return;
            }

            Release();
            InventoryGui.instance?.Hide();
        }

        private void OnDisable() => Release();

        private void OnDestroy() => Release();

        private void Release()
        {
            if (_blocking)
            {
                _blocking = false;
                GUIManager.BlockInput(false);
            }
        }
    }
}
