using UnityEngine;

public enum MotionHorizontalSpace
{
    RealWorldMeters,
    NormalizedScreenX,
}

public abstract class MotionInputProvider : MonoBehaviour
{
    public abstract string ProviderName { get; }
    public abstract bool IsProviderAvailable { get; }
    public abstract bool IsTracked();
    public abstract bool TryGetHorizontalPosition(out float positionX, out MotionHorizontalSpace positionSpace);
    public abstract bool GetJumpInput();

    public virtual Texture GetDebugTexture()
    {
        return null;
    }
}