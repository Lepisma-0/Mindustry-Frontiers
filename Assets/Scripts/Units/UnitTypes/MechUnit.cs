using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Frontiers.Content;

public class MechUnit : Unit {
    public new MechUnitType Type { get => (MechUnitType)base.Type; protected set => base.Type = value; }

    protected override void Update() {
        base.Update();
    }

    protected override void FixedUpdate() {
        base.FixedUpdate();
    }
}