using System;

// Tiny message bus for short on-screen notices (EmergencyUI shows them as a toast).
public static class Notice
{
    public static event Action<string> Posted;
    public static void Show(string message) { Posted?.Invoke(message); }
}
