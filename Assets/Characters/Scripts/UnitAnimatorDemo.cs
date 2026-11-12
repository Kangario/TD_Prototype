// Витрина для сцены CharacterLineup: в Play юнит по кругу показывает все действия своего набора анимаций.
// В игре не нужен - там UnitAnimator вызывает UnitView по событиям логики.
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(UnitAnimator))]
public class UnitAnimatorDemo : MonoBehaviour
{
    public float attackInterval = 1f;
    public float walkSpeed = 1.2f;
    public float runSpeed = 3f;

    UnitAnimator _ua;
    float _speed;

    IEnumerator Start()
    {
        _ua = GetComponent<UnitAnimator>();
        yield return new WaitForSeconds(Random.Range(0f, 0.6f));
        while (true)
        {
            yield return Move(0f, 2f);
            yield return Move(walkSpeed, 2.5f);
            yield return Move(runSpeed, 2.5f);
            yield return Move(0f, 0.6f);
            for (int i = 0; i < 4; i++) { _ua.PlayAttack(attackInterval); yield return new WaitForSeconds(attackInterval); }
            _ua.PlayHit(); yield return new WaitForSeconds(0.8f);
            for (int k = 0; k < 3; k++) { _ua.PlayCast((UnitAnimator.CastKind)k); yield return new WaitForSeconds(1.5f); }
            _ua.PlayWeaponSwap(); yield return new WaitForSeconds(1.5f);
            _ua.PlayEmote(); yield return new WaitForSeconds(2f);
            _ua.SetDead(true); yield return new WaitForSeconds(3.5f);
            _ua.SetDead(false); yield return new WaitForSeconds(1f);
        }
    }

    IEnumerator Move(float speed, float seconds)
    {
        _speed = speed;
        yield return new WaitForSeconds(seconds);
    }

    void Update()
    {
        if (_ua != null) _ua.SetMoveSpeed(_speed);
    }
}
