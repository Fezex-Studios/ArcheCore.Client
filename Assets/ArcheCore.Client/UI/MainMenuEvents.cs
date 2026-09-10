using System;
using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuEvents : MonoBehaviour
{
    
    [SerializeField] UIDocument _document;
    private Button _button;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    private void Awake()
    {
        _document.GetComponent<UIDocument>();
        _button = _document.rootVisualElement.Q<Button>("ConnectBtn") as Button;
        _button.RegisterCallback<ClickEvent>(OnGameClick);
    }

    private void OnGameClick(ClickEvent evt)
    {
        Debug.Log("Connecting to Server");
    }

    private void OnDisable()
    {
        _button.UnregisterCallback<ClickEvent>(OnGameClick);
    }


    // Update is called once per frame
    void Update()
    {
        
    }
}
