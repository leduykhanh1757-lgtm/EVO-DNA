using UnityEngine;

namespace EvoDNA.Interaction
{
    public interface IInteractable
    {
        string GetInteractPrompt();
        void Interact(GameObject interactor);
    }
}

