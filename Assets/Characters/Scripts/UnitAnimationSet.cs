// Набор анимаций под оружие: Override Controller + родная скорость шага и бега.
// Скорости замеряет Tools > P1 Characters > Rebuild Animation Controllers (прогон root motion клипа Walk/Run),
// потому что AnimationClip.averageSpeed у Humanoid-клипов Mixamo занижен до 30%. Значения - при humanScale = 1,
// UnitAnimator умножает их на humanScale конкретного персонажа.
using UnityEngine;

[CreateAssetMenu(menuName = "P1/Unit Animation Set", fileName = "AnimationSet")]
public class UnitAnimationSet : ScriptableObject
{
    public AnimatorOverrideController controller;
    [Tooltip("Родная скорость клипа Walk, м/с при humanScale = 1 (заполняется при сборке)")]
    public float walkSpeed = 1.4f;
    [Tooltip("Родная скорость клипа Run, м/с при humanScale = 1 (заполняется при сборке)")]
    public float runSpeed = 3.5f;
}
