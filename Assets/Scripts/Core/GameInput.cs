using UnityEngine;
using UnityEngine.InputSystem;

namespace TheOtherSide
{
    /// <summary>
    /// Loads the project's Input Action Asset by map/action name.
    /// Does not depend on generated C# wrappers.
    /// </summary>
    public static class GameInput
    {
        public static InputAction Move { get; private set; }
        public static InputAction Look { get; private set; }
        public static InputAction Sprint { get; private set; }
        public static InputAction Jump { get; private set; }
        public static InputAction Interact { get; private set; }
        public static InputAction SwitchDimension { get; private set; }
        public static InputAction TimeFreeze { get; private set; }
        public static InputAction Cancel { get; private set; }

        public static bool UsedFallbackSwitchDimension { get; private set; }

        public static bool IsReady => Move != null;

        static InputAction _ownedSwitchAction;
        static InputAction _ownedTimeFreezeAction;

        public static void Initialize()
        {
            if (Move == null)
                BindCoreActions();

            if (SwitchDimension == null)
                BindSwitchDimension();

            if (TimeFreeze == null)
                BindTimeFreeze();

            EnableGameplay();
        }

        static void BindCoreActions()
        {
            InputActionAsset asset = ResolveActionAsset();
            if (asset == null)
            {
                Debug.LogError("GameInput: could not find InputSystem_Actions. Assign it in Project Settings > Input System Package.");
                return;
            }

            InputActionMap player = asset.FindActionMap("Player", true);
            Move = player.FindAction("Move", true);
            Look = player.FindAction("Look", true);
            Sprint = player.FindAction("Sprint", true);
            Jump = player.FindAction("Jump", true);
            Interact = player.FindAction("Interact", true);
            Cancel = asset.FindActionMap("UI", true).FindAction("Cancel", true);
            SwitchDimension = player.FindAction("SwitchDimension");
        }

        static void BindSwitchDimension()
        {
            if (SwitchDimension != null)
                return;

            InputActionAsset asset = ResolveActionAsset();
            InputActionMap player = asset != null ? asset.FindActionMap("Player") : null;
            SwitchDimension = player != null ? player.FindAction("SwitchDimension") : null;

            if (SwitchDimension != null)
                return;

            UsedFallbackSwitchDimension = true;
            _ownedSwitchAction = new InputAction("SwitchDimension", InputActionType.Button);
            _ownedSwitchAction.AddBinding("<Keyboard>/q");
            _ownedSwitchAction.AddBinding("<Gamepad>/leftShoulder");
            SwitchDimension = _ownedSwitchAction;
            Debug.LogWarning("GameInput: SwitchDimension action missing from InputSystem_Actions. Using fallback Q / Left Shoulder.");
        }

        static void BindTimeFreeze()
        {
            if (TimeFreeze != null)
                return;

            InputActionAsset asset = ResolveActionAsset();
            InputActionMap player = asset != null ? asset.FindActionMap("Player") : null;
            TimeFreeze = player != null ? player.FindAction("TimeFreeze") : null;

            if (TimeFreeze != null)
                return;

            _ownedTimeFreezeAction = new InputAction("TimeFreeze", InputActionType.Button);
            _ownedTimeFreezeAction.AddBinding("<Keyboard>/f");
            _ownedTimeFreezeAction.AddBinding("<Gamepad>/rightShoulder");
            TimeFreeze = _ownedTimeFreezeAction;
            Debug.LogWarning("GameInput: TimeFreeze action missing from InputSystem_Actions. Using fallback F / Right Shoulder.");
        }

        public static void EnableGameplay()
        {
            Move?.Enable();
            Look?.Enable();
            Sprint?.Enable();
            Jump?.Enable();
            Interact?.Enable();
            SwitchDimension?.Enable();
            TimeFreeze?.Enable();
            Cancel?.Enable();
        }

        public static void DisableGameplay()
        {
            Move?.Disable();
            Look?.Disable();
            Sprint?.Disable();
            Jump?.Disable();
            Interact?.Disable();
            SwitchDimension?.Disable();
            TimeFreeze?.Disable();
        }

        public static Vector2 MoveValue => IsReady ? Move.ReadValue<Vector2>() : Vector2.zero;

        public static Vector2 LookValue => IsReady ? Look.ReadValue<Vector2>() : Vector2.zero;
        public static bool SprintHeld => Sprint != null && Sprint.IsPressed();
        public static bool JumpPressed => Jump != null && Jump.WasPressedThisFrame();
        public static bool CancelPressed => Cancel != null && Cancel.WasPressedThisFrame();
        public static bool SwitchDimensionPressed => SwitchDimension != null && SwitchDimension.WasPressedThisFrame();
        public static bool TimeFreezePressed => TimeFreeze != null && TimeFreeze.WasPressedThisFrame();

        static bool _interactHeld;

        /// <summary>
        /// Interact is bound to Keyboard/E and Gamepad buttonNorth.
        /// The asset also has a Hold interaction, so we read bound controls directly for a tap.
        /// </summary>
        public static bool InteractPressed
        {
            get
            {
                if (Interact == null)
                    return false;

                bool down = false;
                var controls = Interact.controls;
                for (int i = 0; i < controls.Count; i++)
                {
                    if (controls[i].IsPressed())
                    {
                        down = true;
                        break;
                    }
                }

                bool pressed = down && !_interactHeld;
                _interactHeld = down;
                return pressed;
            }
        }

        static InputActionAsset ResolveActionAsset()
        {
            if (InputSystem.actions != null)
                return InputSystem.actions;

            InputActionAsset[] loaded = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            for (int i = 0; i < loaded.Length; i++)
            {
                if (loaded[i] != null && loaded[i].name == "InputSystem_Actions")
                    return loaded[i];
            }

            return null;
        }
    }
}
