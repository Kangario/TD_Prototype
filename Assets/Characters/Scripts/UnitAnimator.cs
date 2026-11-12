// Связка событий логики с Animator юнита. Контроллер: Unit_Base (слоты) + Override на набор оружия (Unit_Sword, Unit_Bow...).
// UnitView зовёт методы по событиям:
//   SetMoveSpeed(скорость м/с)  - каждый кадр (подбирает idle/walk/run и темп шага, чтобы ноги не скользили)
//   PlayAttack(AttackInterval)  - AttackStarted (случайный вариант, ускоряется под интервал оружия)
//   PlayCast(вид)               - SkillTriggered (Attack: Метеор/Залп, Support: Молитва/Благословение, Defense: Щит)
//   PlayHit()                   - DamageDealt по юниту (лучше только крупный урон/крит, иначе толпа дёргается)
//   PlayWeaponSwap()            - WeaponChanged;  SetAnimationSet(набор) - сменить набор анимаций под оружие
//   PlayEmote()                 - рёв босса и т.п.;  SetDead(true/false) - UnitDied / возврат юнита в пул
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class UnitAnimator : MonoBehaviour
{
    public const string SlotPrefix = "Slot_";
    public static readonly string[] Slots =
    {
        "Idle_1", "Idle_2", "Walk", "Run",
        "Attack_1", "Attack_2", "Attack_3",
        "Cast_1", "Cast_2", "Cast_3",
        "Hit_1", "Hit_2", "Hit_3",
        "Death_1", "Death_2", "Death_3",
        "WeaponSwap", "Emote",
    };

    public enum CastKind { Attack = 0, Support = 1, Defense = 2 }

    [Tooltip("Набор анимаций оружия (Assets/Characters/Animations/Sets). Пусто - контроллер, что уже стоит в Animator")]
    public UnitAnimationSet animationSet;
    [Tooltip("Во сколько раз максимум ускорять атаку, чтобы уложить её в интервал")]
    public float maxAttackSpeedUp = 2.5f;
    [Tooltip("Навык проигрывается не дольше, с")]
    public float maxCastDuration = 1.2f;
    [Tooltip("Разброс темпа idle, чтобы толпа не дышала в такт")]
    [Range(0f, 0.2f)] public float idleJitter = 0.08f;

    static readonly int pMoveBlend = Animator.StringToHash("MoveBlend");
    static readonly int pLocoSpeed = Animator.StringToHash("LocoSpeed");
    static readonly int pIdleVariant = Animator.StringToHash("IdleVariant");
    static readonly int pAttackVariant = Animator.StringToHash("AttackVariant");
    static readonly int pAttackSpeed = Animator.StringToHash("AttackSpeed");
    static readonly int pCastType = Animator.StringToHash("CastType");
    static readonly int pCastSpeed = Animator.StringToHash("CastSpeed");
    static readonly int pHitVariant = Animator.StringToHash("HitVariant");
    static readonly int pDeathVariant = Animator.StringToHash("DeathVariant");
    static readonly int pAttack = Animator.StringToHash("Attack");
    static readonly int pCast = Animator.StringToHash("Cast");
    static readonly int pHit = Animator.StringToHash("Hit");
    static readonly int pSwap = Animator.StringToHash("WeaponSwap");
    static readonly int pEmote = Animator.StringToHash("Emote");
    static readonly int pDead = Animator.StringToHash("Dead");

    Animator _anim;
    readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();
    readonly List<int> _idle = new List<int>(), _attacks = new List<int>(), _hits = new List<int>(), _deaths = new List<int>();
    float _walkSpeed = 1.4f, _runSpeed = 3.5f, _idleRate = 1f;

    /// Родная скорость клипов текущего набора на этом персонаже, м/с
    public float WalkSpeed => _walkSpeed;
    public float RunSpeed => _runSpeed;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        _anim.applyRootMotion = false;
        _idleRate = 1f + Random.Range(-idleJitter, idleJitter);
        if (animationSet != null) SetAnimationSet(animationSet);
        else Cache();
    }

    void OnEnable()
    {
        if (_anim == null || _anim.runtimeAnimatorController == null) return;
        _anim.SetFloat(pIdleVariant, Pick(_idle));
        _anim.Play(0, 0, Random.value);   // случайная фаза текущего состояния
    }

    public void SetAnimationSet(UnitAnimationSet set)
    {
        animationSet = set;
        if (set != null && set.controller != null && _anim.runtimeAnimatorController != set.controller)
            _anim.runtimeAnimatorController = set.controller;
        Cache();
    }

    /// Скорость юнита из логики, м/с.
    public void SetMoveSpeed(float speed)
    {
        float w = _walkSpeed, r = _runSpeed, blend, rate = 1f;
        if (speed < 0.05f) { blend = 0f; rate = _idleRate; }
        else if (speed <= w)
        {
            float k = speed / w;                         // медленнее родной ходьбы: замедляем шаг,
            if (k < 0.5f) { blend = k * 2f; rate = 0.5f; } // а совсем медленно - подмешиваем idle
            else { blend = 1f; rate = k; }
        }
        else if (speed < r) blend = 1f + (speed - w) / (r - w);   // смесь walk/run даёт ровно нужную скорость
        else { blend = 2f; rate = speed / r; }
        _anim.SetFloat(pMoveBlend, blend, 0.08f, Time.deltaTime);
        _anim.SetFloat(pLocoSpeed, rate);
    }

    public void PlayAttack(float attackInterval)
    {
        int i = Pick(_attacks);
        float len = Len("Attack_" + (i + 1));
        _anim.SetFloat(pAttackVariant, i);
        _anim.SetFloat(pAttackSpeed, attackInterval > 0f ? Mathf.Clamp(len / attackInterval, 1f, maxAttackSpeedUp) : 1f);
        _anim.SetTrigger(pAttack);
    }

    public void PlayCast(CastKind kind = CastKind.Attack)
    {
        int i = (int)kind;
        _anim.SetFloat(pCastType, i);
        _anim.SetFloat(pCastSpeed, Mathf.Max(1f, Len("Cast_" + (i + 1)) / maxCastDuration));
        _anim.SetTrigger(pCast);
    }

    public void PlayHit()
    {
        _anim.SetFloat(pHitVariant, Pick(_hits));
        _anim.SetTrigger(pHit);
    }

    public void PlayWeaponSwap() => _anim.SetTrigger(pSwap);
    public void PlayEmote() => _anim.SetTrigger(pEmote);

    public void SetDead(bool dead)
    {
        if (dead) _anim.SetFloat(pDeathVariant, Pick(_deaths));
        else
        {
            _anim.ResetTrigger(pAttack); _anim.ResetTrigger(pCast); _anim.ResetTrigger(pHit);
            _anim.ResetTrigger(pSwap); _anim.ResetTrigger(pEmote);
        }
        _anim.SetBool(pDead, dead);
    }

    // ------------------------------------------------------------------
    void Cache()
    {
        _clips.Clear();
        if (_anim.runtimeAnimatorController is AnimatorOverrideController oc)
        {
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(oc.overridesCount);
            oc.GetOverrides(pairs);
            foreach (var p in pairs)
                if (p.Key != null && p.Key.name.StartsWith(SlotPrefix))
                    _clips[p.Key.name.Substring(SlotPrefix.Length)] = p.Value != null ? p.Value : p.Key;
        }
        Distinct(_idle, "Idle_", 2);
        Distinct(_attacks, "Attack_", 3);
        Distinct(_hits, "Hit_", 3);
        Distinct(_deaths, "Death_", 3);
        // root motion гуманоида масштабируется под рост персонажа
        float scale = _anim.isHuman ? _anim.humanScale : 1f;
        _walkSpeed = (animationSet != null && animationSet.walkSpeed > 0.05f ? animationSet.walkSpeed : 1.4f) * scale;
        _runSpeed = Mathf.Max((animationSet != null && animationSet.runSpeed > 0.05f ? animationSet.runSpeed : 3.5f) * scale,
                              _walkSpeed + 0.2f);
    }

    /// Индексы вариантов с разными клипами (одинаковые не удваивают шанс)
    void Distinct(List<int> idx, string prefix, int count)
    {
        idx.Clear();
        var seen = new HashSet<AnimationClip>();
        for (int i = 0; i < count; i++)
            if (_clips.TryGetValue(prefix + (i + 1), out var c) && c != null && c.length > 0f && seen.Add(c))
                idx.Add(i);
        if (idx.Count == 0) idx.Add(0);
    }

    float Len(string slot) => _clips.TryGetValue(slot, out var c) && c != null && c.length > 0f ? c.length : 1f;
    static int Pick(List<int> idx) => idx[Random.Range(0, idx.Count)];
}
