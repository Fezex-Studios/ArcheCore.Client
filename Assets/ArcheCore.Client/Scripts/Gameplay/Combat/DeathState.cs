namespace ArcheCore.Client.Gameplay.Combat
{
    /// <summary>
    /// Is the local player dead? One flag, read by anything that must stop
    /// while you're down - movement input today, skills and interaction
    /// later. The server enforces all of it anyway; this is so the client
    /// doesn't send requests it knows will be refused, and so the character
    /// doesn't run around while dead.
    /// </summary>
    public static class DeathState
    {
        public static bool IsDead { get; private set; }

        public static void SetDead(bool dead) => IsDead = dead;
    }
}
