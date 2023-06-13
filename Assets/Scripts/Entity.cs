using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Photon.Pun;
using Photon.Realtime;
using Photon.Pun.UtilityScripts;
using Frontiers.Content;
using Frontiers.Assets;
using Frontiers.Teams;

public abstract class Entity : SyncronizableObject, IDamageable, IInventory {
    public event EventHandler<EventArgs> OnDestroyed;

    private GameObject[] fires;

    protected Color teamColor;
    protected EntityType Type;
    protected Inventory inventory;

    protected int id;
    protected byte teamCode;
    protected float health;

    public bool hasInventory = false;
    public float size;

    public bool wasDestroyed = false;
    int fireCount;

    public override float[] GetSyncValues() {
        float[] values = base.GetSyncValues();
        values[1] = health;
        return values;
    }

    public override void ApplySyncValues(float[] values) {
        base.ApplySyncValues(values);
        health = values[1];
    }

    public virtual void Set<T>(Vector2 position, Quaternion rotation, T type, int id, byte teamCode) where T : EntityType {
        this.id = id;
        this.teamCode = teamCode;
        this.Type = type;

        fires = new GameObject[Type.maximumFires];
        teamColor = TeamUtilities.GetTeamColor(teamCode);

        SetLayerAllChildren(transform, GetTeamLayer());
        SetInventory();
        SetSprites();

        syncValues = 2;
    }

    protected Color CellColor() {
        float hp = GetHealthPercent();
        float sin = Mathf.Sin(5f * Time.time * (2f - hp));
        return Color.Lerp(Color.black, teamColor, 1 - Mathf.Max(sin - hp * sin, 0));
    }

    protected virtual int GetTeamLayer(bool ignore = false) => TeamUtilities.GetTeamLayer(teamCode, ignore);

    protected virtual int GetTeamMask(bool ignore = false) => TeamUtilities.GetTeamMask(teamCode, ignore);

    public virtual Vector2 GetPosition() => transform.position;

    public virtual Vector2 GetPredictedPosition(Vector2 origin, Vector2 velocity) => transform.position;

    public Inventory GetInventory() => inventory;

    public byte GetTeam() => TeamUtilities.GetTeamByCode(teamCode);

    public bool IsLocalTeam() => TeamUtilities.GetLocalTeam() == GetTeam();

    public abstract EntityType GetEntityType();

    public virtual void SetInventory() {
        if (inventory != null) inventory.OnAmountChanged += OnInventoryValueChange;
    }

    public static void SetLayerAllChildren(Transform root, int layer) {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children) child.gameObject.layer = layer;
    }

    public static void SetOptionalSprite(Transform transform, Sprite sprite) {
        SpriteRenderer spriteRenderer = transform.GetComponent<SpriteRenderer>();

        if (!sprite) Destroy(transform.gameObject);
        if (!sprite || !spriteRenderer) return;

        spriteRenderer.sprite = sprite;
    }

    public static void SetOptionalSprite(Transform transform, Sprite sprite, out SpriteRenderer finalSpriteRenderer) {
        SpriteRenderer spriteRenderer = finalSpriteRenderer = transform.GetComponent<SpriteRenderer>();

        if (!sprite) Destroy(transform.gameObject);
        if (!sprite || !spriteRenderer) return;

        spriteRenderer.sprite = sprite;
    }

    protected abstract void SetSprites();

    public abstract void OnInventoryValueChange(object sender, EventArgs e);

    public virtual bool CanReciveItem(Item item) {
        return hasInventory && inventory != null && inventory.Allowed(item);
    }

    public float GetHealthPercent() {
        return health / Type.health;
    }

    public void Damage(float amount) {
        health -= amount;
        OnHealthChange();
    }

    public void SetHealth(float health) {
        this.health = health;
        OnHealthChange();
    }

    private void OnHealthChange() {
        if (health <= 0) {
            if (this is Unit unit) Client.DestroyUnit(unit, true);
            else if (this is Block block) Client.DestroyBlock(block, true);
        }

        if (!Type.canGetOnFire) return;
        fireCount = Mathf.CeilToInt(Type.maximumFires * Mathf.Clamp(0.5f - GetHealthPercent(), 0, 0.5f) * 2);

        if (fireCount == 0) return;

        if (!fires[fireCount - 1]) {
            Vector3 worldPosition = transform.position + (new Vector3(UnityEngine.Random.Range(0f, 0.4f), UnityEngine.Random.Range(0f, 0.4f), 0) * size);
            fires[fireCount - 1] = Instantiate(AssetLoader.GetPrefab("HitSmokeEffect"), worldPosition, Quaternion.Euler(0, 0, UnityEngine.Random.Range(0f, 359.99f)), transform);
            fires[fireCount - 1].transform.localScale = Vector3.one * size;
        }
    }

    public virtual void OnDestroy() {
        if (!gameObject.scene.isLoaded) return;
        OnDestroyed?.Invoke(this, EventArgs.Empty);
    }

    public override string ToString() {
        string data = "";
        data += SyncID + ":";
        data += Type.id + ":";
        data += teamCode + ":";
        data += health + ":";
        return data;
    }

    public bool IsBuilding() {
        return Content.TypeEquals(Type.GetType(), typeof(BlockType));
    }
}