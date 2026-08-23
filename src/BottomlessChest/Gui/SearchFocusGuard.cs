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
    internal sealed class SearchFocusGuard : MonoBehaviour
    {
        private InputField _input;
        private bool _blocking;

        private void Awake()
        {
            _input = GetComponent<InputField>();
        }

        private void Update()
        {
            if (_input == null)
            {
                Release();
                return;
            }

            if (_input.isFocused == _blocking)
            {
                return;
            }

            _blocking = _input.isFocused;
            GUIManager.BlockInput(_blocking);
            Plugin.Log.LogDebug($"Search box focus: {_blocking}");
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
