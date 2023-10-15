using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Frontiers.Content;
using Frontiers.Content.Maps;
using Frontiers.Assets;
using Frontiers.Content.SoundEffects;
using System;

public class MapManager : MonoBehaviour {
    public static MapManager Instance;
    public static Map Map;

    public static Vector2Int mouseGridPos;
    public static bool mouseGridAllowsPlace;
    public static int nextID;

    public Vector2 shardCorePosition, cruxCorePosition;

    public SpriteRenderer spriteRenderer;
    private GameObject blockPrefab;
    private GameObject unitPrefab;

    public bool hasQuack = true;
    private AudioSource quack;

    private void Update() {
        BulletManager.UpdateBullets();
    }

    public static void InitializeMapManager() {
        Instance = FindObjectOfType<MapManager>();
        Instance.Setup();
    }

    public void Setup() {
        blockPrefab = AssetLoader.GetPrefab("BlockPrefab");
        unitPrefab = AssetLoader.GetPrefab("UnitPrefab");
        MapLoader.OnMapLoaded += OnMapLoaded;

        if (!hasQuack) return;

        // Quack
        Transform quack = new GameObject("quack", typeof(AudioSource)).transform;
        this.quack = quack.GetComponent<AudioSource>();
        this.quack.spatialBlend = 1f;
        this.quack.maxDistance = 100f;
        this.quack.playOnAwake = false;
        this.quack.clip = Sounds.quack.clip;
        Invoke(nameof(Quack), 3f);
    }

    public void Quack() {
        quack.transform.position = (Vector2)Camera.main.transform.position + (UnityEngine.Random.insideUnitCircle * UnityEngine.Random.Range(25f, 75f));
        quack.Play();
        if (hasQuack) Invoke(nameof(Quack), UnityEngine.Random.Range(1f, 4f));
    }

    public void OnMapLoaded(object sender, MapLoader.MapLoadedEventArgs e) {
        Map = e.loadedMap;
    }

    public void SaveMap() {
        MapLoader.SaveMap(Map);
    }

    public static bool IsLoaded() {
        return Map != null;
    }

    public void UpdateMapManager() {
        if (Map == null) return;
        Content selectedContent = PlayerContentSelector.SelectedContent;
        int size = selectedContent == null ? 1 : TypeEquals(selectedContent.GetType(), typeof(BlockType)) ? ((BlockType)selectedContent).size : 1;
        Vector2Int mouseGridPos = Vector2Int.CeilToInt(PlayerManager.mousePos - (Vector3.one * 0.5f) - (0.5f * size * Vector3.one));

        mouseGridAllowsPlace = Map.InBounds(mouseGridPos) && Map.CanPlaceBlockAt(mouseGridPos, size);
        MapManager.mouseGridPos = mouseGridPos;
    }

    public void InitializeCores() {
        Client.CreateBlock(shardCorePosition, 0, Blocks.coreShard, 1);
        Client.CreateBlock(cruxCorePosition, 0, Blocks.coreShard, 2);
    }

    public static bool TypeEquals(Type target, Type reference) => target == reference || target.IsSubclassOf(reference);

    public int GetID() {
        nextID++;
        return nextID - 1;
    }

    public Block InstantiateBlock(Vector2 position, int orientation, short contentID, short syncID, byte teamCode) {
        Vector2Int gridPosition = Vector2Int.CeilToInt(position);
        BlockType blockType = (BlockType)ContentLoader.GetContentById(contentID);

        GameObject blockGameObject = Instantiate(blockPrefab, (Vector2)gridPosition, Quaternion.identity);
        Block block = (Block)blockGameObject.AddComponent(blockType.type);

        block.Set(syncID);
        block.Set(gridPosition, Quaternion.Euler(0, 0, orientation * 90f), blockType, GetID(), teamCode);

        return block;
    }

    public void DeleteBlock(Block block, bool destroyed) {
        block.wasDestroyed = destroyed;
        Destroy(block.gameObject);
    }

    public Unit InstantiateUnit(Vector2 position, float rotation, short contentID, short syncID, byte teamCode) {
        UnitType unitType = ContentLoader.GetContentById(contentID) as UnitType;
        GameObject unitGameObject = Instantiate(unitPrefab, position, Quaternion.identity);
        Unit unit = (Unit)unitGameObject.AddComponent(unitType.type);

        unit.Set(syncID);
        unit.Set(position, Quaternion.Euler(0, 0, rotation), unitType, GetID(), teamCode);
        unit.SetVelocity(unitGameObject.transform.forward * 0.5f);

        return unit;
    }

    public void DeleteUnit(Unit unit, bool destroyed) {
        unit.wasDestroyed = destroyed;
        Destroy(unit.gameObject);
    }
}