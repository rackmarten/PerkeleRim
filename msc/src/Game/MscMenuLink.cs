using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's options menu used from Skyrim (F1; docs/PROTOCOL.md, "MSC's menu in Skyrim"). While
    // Skyrim asks for it (InputState kInputMscMenu) MSC opens it the way its own Escape does (the
    // Systems/Options :: Open Menu FSM reads Escape through the input shim) and PlayerLink leaves it
    // open; HandLayerSender renders its camera (Systems/OptionsMenu/CAM) into the shared texture
    // instead of the hands, and the shim's mouse position is Skyrim's cursor. The quit buttons' colliders are off
    // meanwhile, so clicking them does nothing. Skyrim dropping the request closes it again
    // (PlayerLink.CloseMenu, through Resume); MSC's Resume drops kPlayerMscMenu, and Skyrim closes
    // its side.
    public sealed class MscMenuLink
    {
        const string MenuPath = "Systems/OptionsMenu";
        const string ButtonsPath = "Menu";  // below MenuPath: Btn_Resume, Btn_Quit, ...

        readonly Action<string> log;
        readonly OptionsMenu rules = new OptionsMenu();
        readonly List<Collider> switchedOff = new List<Collider>();
        Transform menu;
        Camera menuCamera;
        FsmBool inMenu;
        bool wasOpen;
        bool quitOff;

        public MscMenuLink(Action<string> log) { this.log = log; }

        // Skyrim has the menu: PlayerLink doesn't close it.
        public bool Holding => rules.Holding;

        // MSC's menu is up (PlayerState kPlayerMscMenu).
        public bool Open { get; private set; }

        // The menu's camera while Skyrim shows the menu and it's up, else null.
        public Camera Camera => rules.Holding && Open ? menuCamera : null;

        // LateUpdate, before PlayerLink.CloseMenu. requested: MSC has the player and Skyrim asks.
        public void Update(bool requested)
        {
            Find();
            Open = inMenu != null && inMenu.Value && menu != null && menu.gameObject.activeInHierarchy;
            if (Open != wasOpen)
            {
                wasOpen = Open;
                if (rules.Holding || requested) log(Open ? "msc menu: up for Skyrim" : "msc menu: closed");
            }
            if (rules.Step(requested, Open, Time.realtimeSinceStartup))
            {
                InputShim.Pulse(KeyCode.Escape);
                log("msc menu: Skyrim asks for it, pressing Escape");
            }
            if (rules.Holding && Open) SwitchOffQuit();
            else SwitchOnQuit();
        }

        void Find()
        {
            if (inMenu == null) inMenu = FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu");
            if (menu != null) return;
            var go = GameObject.Find("Systems");
            menu = go != null ? go.transform.Find("OptionsMenu") : null;
            var cam = menu != null ? menu.Find("CAM") : null;
            menuCamera = cam != null ? cam.GetComponent<Camera>() : null;
            if (menu != null) log($"msc menu: found {MenuPath}, camera {(menuCamera != null ? "CAM" : "missing")}");
        }

        void SwitchOffQuit()
        {
            if (quitOff) return;
            quitOff = true;
            var buttons = menu.Find(ButtonsPath);
            if (buttons == null) return;
            foreach (Transform button in buttons)
            {
                if (!OptionsMenu.IsIgnored(button.name)) continue;
                foreach (var c in button.GetComponentsInChildren<Collider>(true))
                    if (c.enabled) { c.enabled = false; switchedOff.Add(c); }
            }
            log($"msc menu: {switchedOff.Count} quit button colliders off while Skyrim shows the menu");
        }

        void SwitchOnQuit()
        {
            if (!quitOff) return;
            quitOff = false;
            foreach (var c in switchedOff)
                if (c != null) c.enabled = true;
            switchedOff.Clear();
        }
    }
}
