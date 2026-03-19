using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace NavKeypad { 
public class KeypadInteractionFPV : MonoBehaviour
{
    private Camera cam;
    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        EnsureCompatibleEventSystem();
    }

    private void OnEnable()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    private void Update()
    {
        if (cam == null)
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
        }

        Vector2 pointerPos;
        bool pressDown;
#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse == null) return;
        pointerPos = mouse.position.ReadValue();
        pressDown = mouse.leftButton.wasPressedThisFrame;
#else
        pointerPos = Input.mousePosition;
        pressDown = Input.GetMouseButtonDown(0);
#endif

        if (pressDown)
        {
            var ray = cam.ScreenPointToRay(pointerPos);
            if (Physics.Raycast(ray, out var hit))
            {
                if (hit.collider.TryGetComponent(out KeypadButton keypadButton))
                {
                    keypadButton.PressButton();
                }
            }
        }
    }

    private static void EnsureCompatibleEventSystem()
    {
        var es = EventSystem.current;
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            es = go.AddComponent<EventSystem>();
        }

        var standalone = es.GetComponent<StandaloneInputModule>();
        if (standalone != null)
            standalone.enabled = false;

#if ENABLE_INPUT_SYSTEM
        if (es.GetComponent<InputSystemUIInputModule>() == null)
            es.gameObject.AddComponent<InputSystemUIInputModule>();
#else
        if (es.GetComponent<StandaloneInputModule>() == null)
            es.gameObject.AddComponent<StandaloneInputModule>();
#endif
    }
}
}