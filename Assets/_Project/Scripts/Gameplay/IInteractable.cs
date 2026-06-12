namespace Ngj10.Gameplay
{
    /// <summary>
    /// Something the player can act on with the interact key (E).
    /// </summary>
    public interface IInteractable
    {
        /// <summary>World position used to measure distance to the player.</summary>
        UnityEngine.Vector2 Position { get; }

        /// <summary>True when interaction is currently allowed for this player.</summary>
        bool CanInteract(PlayerController player);

        void Interact(PlayerController player);
    }
}
