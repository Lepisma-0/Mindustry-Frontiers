using Photon.Pun;
using Photon.Pun.UtilityScripts;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using UnityEngine;
using Frontiers.Content;
using Frontiers.Content.Maps;
using Frontiers.Settings;
using Frontiers.Squadrons;
using Frontiers.Teams;
using Frontiers.Pooling;
using Frontiers.Animations;
using Frontiers.Assets;
using CI.QuickSave.Core.Serialisers;
using CI.QuickSave.Core.Settings;
using CI.QuickSave.Core.Storage;
using CI.QuickSave.Core.Converters;
using CI.QuickSave;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using Anim = Frontiers.Animations.Anim;
using Animator = Frontiers.Animations.Animator;
using Animation = Frontiers.Animations.Animation;
using System.Runtime.Serialization.Formatters.Binary;

namespace Frontiers.Animations {
    public class Animator {
        readonly Dictionary<Animation.Case, Anim> animations = new Dictionary<Animation.Case, Anim>();

        public void AddAnimation(Anim anim) {
            if (animations.ContainsKey(anim.GetCase())) return;
            animations.Add(anim.GetCase(), anim);
        }

        public void NextFrame() {
            if (animations.Count == 0) return;
            animations[0].NextFrame();
        }

        public void NextFrame(Animation.Case useCase) {
            if (!animations.ContainsKey(useCase)) return;
            animations[useCase].NextFrame();
        }
    }

    public class Anim {
        readonly SpriteRenderer animationRenderer;
        readonly Sprite[] animationFrames;

        Animation animation;
        int frame;

        public Anim(string baseName, string layerName, int layerOrder, Transform parent, Animation animation) {
            if (animation.frames == 0) return;
            this.animation = animation;

            // Get all the frames from the animation
            animationFrames = new Sprite[animation.frames];
            for (int i = 0; i < animation.frames; i++) {
                animationFrames[i] = AssetLoader.GetSprite(baseName + animation.name + "-" + i);
            }

            // Create a new gameObject to hold the animation
            GameObject animGameObject = new GameObject("animation" + animation.name, typeof(SpriteRenderer));

            // Set the position && rotation to 0
            animGameObject.transform.parent = parent;
            animGameObject.transform.localPosition = Vector3.zero;
            animGameObject.transform.localRotation = Quaternion.identity;

            // Get the sprite renderer component
            animationRenderer = animGameObject.GetComponent<SpriteRenderer>();
            animationRenderer.sortingLayerName = layerName;
            animationRenderer.sortingOrder = layerOrder;
        }

        public void NextFrame() {
            frame++;
            if (frame >= animation.frames) frame = 0;
            animationRenderer.sprite = animationFrames[frame];
        }

        public Animation.Case GetCase() => animation.useCase;
    }

    public struct Animation {
        public string name;
        public int frames;
        public Case useCase;

        public enum Case {
            Reload,
            Shoot
        }

        public Animation(string name, int frames, Case useCase) {
            this.name = name;
            this.frames = frames;
            this.useCase = useCase;
        }
    }
}

namespace Frontiers.Pooling {
    public class PoolManager : MonoBehaviour {
        public static Dictionary<string, GameObjectPool> allPools = new Dictionary<string, GameObjectPool>();

        public static GameObjectPool NewPool(GameObject prefab, int targetAmount, string name = null) {
            GameObjectPool newPool = new GameObjectPool(prefab, targetAmount);
            if (name == null) name = prefab.name;

            allPools.Add(name, newPool);
            return newPool;
        }

        public static GameObject Pool_CreateGameObject(GameObject prefab, Transform parent = null) {
            return Instantiate(prefab, parent);
        }

        public static void Pool_DestroyGameObject(GameObject gameObject) {
            Destroy(gameObject);
        }
    }

    public class GameObjectPool {
        // The hard limit of gameobjects in the pool, only used if the pool gets too big where creating/destroying a gameobject is better than storing it
        public int targetAmount;

        public GameObject prefab;
        public Queue<GameObject> pooledGameObjects;

        public GameObjectPool(GameObject prefab, int targetAmount) {
            this.prefab = prefab;
            this.targetAmount = targetAmount;
            pooledGameObjects = new Queue<GameObject>();
        }

        public bool CanTake() => pooledGameObjects.Count > 0;

        public bool CanReturn() => targetAmount == -1 || pooledGameObjects.Count < targetAmount;

        public GameObject Take() {
            GameObject gameObject = !CanTake() ? PoolManager.Pool_CreateGameObject(prefab) : pooledGameObjects.Dequeue();
            gameObject.SetActive(true);
            return gameObject;
        }

        public void Return(GameObject gameObject) {
            gameObject.SetActive(false);
            if (CanReturn()) pooledGameObjects.Enqueue(gameObject);
            else PoolManager.Pool_DestroyGameObject(gameObject);
        }
    }

}

namespace Frontiers.Settings {
    public static class State {
        /// <summary>
        /// The time interval each entity should sync their data to other players
        /// </summary>
        public const float SYNC_TIME = 5f;
    }
}

namespace Frontiers.Squadrons {
    public enum Action {
        Idle,
        Attack,
        Move,
        Land,
        TakeOff
    }

    public struct Order {
        public Action action;

        public Vector2 actionPosition;
        public Transform actionTarget;

        public Order(Action action, Vector2 actionPosition, Transform actionTarget = null) {
            this.action = action;
            this.actionPosition = actionPosition;
            this.actionTarget = actionTarget;
        }

        public Vector2 GetActionPosition() => actionPosition == Vector2.zero && actionTarget ? (Vector2)actionTarget.position : actionPosition;
    }

    public struct OrderSeq {
        public List<Order> orderList;

        public OrderSeq(List<Order> orderList) {
            this.orderList = orderList;
        }

        public void OrderComplete() {
            if (orderList.Count == 0) return;
            orderList.RemoveAt(0);
        }

        public Order GetOrder() => orderList.Count == 0 ? new Order(Action.Idle, Vector2.zero) : orderList[0];
    }

    public class DemoOrderSeq {
        public static OrderSeq takeOffAndLand;

        public static void Load() {
            takeOffAndLand = new OrderSeq(new List<Order>() {
                new Order(Action.TakeOff, Vector2.zero),
                new Order(Action.Move, new Vector2(10f, 50f)),
                new Order(Action.Land, Vector2.zero)
            }); 
        }
    }
}

namespace Frontiers.Teams {
    public static class TeamUtilities {
        public static readonly Color LocalTeamColor = new(1f, 0.827451f, 0.4980392f);
        public static readonly Color EnemyTeamColor = new(0.9490196f, 0.3333333f, 0.3333333f);

        public static List<CoreBlock> LocalCoreBlocks = new();
        public static List<CoreBlock> EnemyCoreBlocks = new();

        public static int GetTeamLayer(byte teamCode, bool ignore = false) => LayerMask.NameToLayer((ignore ? "IgnoreTeam" : "CollideTeam") + teamCode);

        public static int GetTeamMask(byte teamCode, bool ignore = false) => LayerMask.GetMask((ignore ? "IgnoreTeam" : "CollideTeam") + teamCode);

        public static int GetEnemyTeamLayer(byte teamCode, bool ignore = false) => GetTeamLayer(GetEnemyTeam(teamCode), ignore);

        public static int GetEnemyTeamMask(byte teamCode, bool ignore = false) => GetTeamMask(GetEnemyTeam(teamCode), ignore);

        public static Color GetTeamColor(byte teamCode) => teamCode == GetLocalTeam() ? LocalTeamColor : EnemyTeamColor;

        public static void AddCoreBlock(CoreBlock coreBlock) {
            if (coreBlock.IsLocalTeam()) LocalCoreBlocks.Add(coreBlock);
            else EnemyCoreBlocks.Add(coreBlock);
        }

        public static void RemoveCoreBlock(CoreBlock coreBlock) {
            if (LocalCoreBlocks.Contains(coreBlock)) LocalCoreBlocks.Remove(coreBlock);
            if (EnemyCoreBlocks.Contains(coreBlock)) EnemyCoreBlocks.Remove(coreBlock);
        }

        public static CoreBlock GetClosestCoreBlock(Vector2 position, byte teamCode) => teamCode == GetLocalTeam() ? GetClosestAllyCoreBlock(position) : GetClosestEnemyCoreBlock(position);

        public static CoreBlock GetClosestAllyCoreBlock(Vector2 position) {       
            float closestDistance = 9999f;
            CoreBlock closestCoreBlock = null;

            foreach(CoreBlock coreBlock in LocalCoreBlocks) {
                float distance = Vector2.Distance(coreBlock.GetGridPosition(), position);

                if (distance <= closestDistance) {
                    closestDistance = distance;
                    closestCoreBlock = coreBlock;
                }
            }

            return closestCoreBlock;
        }

        public static CoreBlock GetClosestEnemyCoreBlock(Vector2 position) {
            float closestDistance = 9999f;
            CoreBlock closestCoreBlock = null;

            foreach (CoreBlock coreBlock in EnemyCoreBlocks) {
                float distance = Vector2.Distance(coreBlock.GetGridPosition(), position);

                if (distance <= closestDistance) {
                    closestDistance = distance;
                    closestCoreBlock = coreBlock;
                }
            }

            return closestCoreBlock;
        }

        public static bool IsMaster() => PhotonNetwork.IsMasterClient;

        public static byte GetLocalTeam() => PhotonNetwork.LocalPlayer.GetPhotonTeam().Code;

        public static byte GetEnemyTeam(byte code) => code == 1 ? GetTeamByCode(2) : GetTeamByCode(1);

        public static byte GetDefaultTeam() => GetTeamByCode(1);

        public static byte GetTeamByCode(byte code) {
            RoomManager.Instance.photonTeamsManager.TryGetTeamByCode(code, out PhotonTeam team);
            return team.Code;
        }

        public static Player[] TryGetTeamMembers(byte code) {
            RoomManager.Instance.photonTeamsManager.TryGetTeamMembers(code, out Player[] members);
            return members;
        }

    }
}

namespace Frontiers.Assets {

    public static class AssetLoader {
        private static Sprite[] sprites;
        private static GameObject[] prefabs;

        private static Object[] assets;

        public static void LoadAssets() {
            sprites = Resources.LoadAll<Sprite>("Sprites");
            prefabs = Resources.LoadAll<GameObject>("Prefabs");
            assets = Resources.LoadAll<Object>("");

            Debug.Log("All Assets Loaded!");
        }

        public static Sprite GetSprite(string name, bool suppressWarnings = false) {
            foreach (Sprite sprite in sprites) if (sprite.name == name) return sprite;

            if (!suppressWarnings) Debug.LogWarning("No sprite was found with the name: " + name);
            return null;
        }

        public static GameObject GetPrefab(string name, bool suppressWarnings = false) {
            foreach (GameObject prefab in prefabs) if (prefab.name == name) return prefab;

            if (!suppressWarnings) Debug.LogWarning("No prefab was found with the name: " + name);
            return null;
        }

        public static T GetAsset<T>(string name, bool suppressWarnings = false) where T : Object {
            foreach (Object prefab in assets) if (prefab.name == name && prefab is T) return prefab as T;

            if (!suppressWarnings) Debug.LogWarning("No asset was found with the name: " + name);
            return null;
        }
    }

    [Serializable]
    public class Wrapper<T> {
        public T[] array;

        public Wrapper(T[] items) {
            this.array = items;
        }
    }

    [Serializable]
    public class Wrapper2D<T> {
        public T[,] array2D;

        public Wrapper2D(T[,] items) {
            this.array2D = items;
        }
    }

    public class TypeWrapper {
        public static Type GetSystemType(string name) => Type.GetType(name + ", Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");

        public static string GetString(Type type) {
            string fullName = type.AssemblyQualifiedName;
            return fullName.Remove(fullName.IndexOf(","));
        }
    }
}

namespace Frontiers.Content {

    public static class ContentLoader {
        public static Dictionary<int, Content> loadedContents;
        public static List<Mod> modList;

        public static void LoadContent() {
            loadedContents = new();
            modList = new List<Mod>();

            Items.Load();
            Tiles.Load();
            Bullets.Load();
            Weapons.Load();
            Units.Load();
            Blocks.Load();

            int baseContents = loadedContents.Count;
            Debug.Log(loadedContents.Count + " Base contents loaded");

            LoadMods();
            Debug.Log(loadedContents.Count - baseContents + " Mod contents loaded from " + modList.Count + " mods");

            InitializeObjectPools();
        }

        public static void InitializeObjectPools() {
            ConveyorBlock.conveyorItemPool = PoolManager.NewPool(Assets.AssetLoader.GetPrefab("conveyorItem"), 100);
            RaycastWeapon.tracerGameObjectPool = PoolManager.NewPool(Assets.AssetLoader.GetPrefab("tPref"), 100);
        }

        public static void HandleContent(Content content) {
            if (GetContentByName(content.name) != null) throw new ArgumentException("Two content objects cannot have the same name! (issue: '" + content.name + "')");

            loadedContents.Add(content.id, content);
        }

        /*
        public static void TEST_SaveContents() {
            foreach(Content content in contentMap) {
                SaveContent(content);
            }

            SaveMod(new Mod());
        }
        */ // Json content/mod generator

        public static void LoadMods() {
            List<string> modsToLoad = GetModNames();
            foreach (string modName in modsToLoad) modList.Add(ReadMod(modName));
            foreach (Mod mod in modList) mod.LoadMod();
        }

        public static List<string> GetModNames() {
            List<string> modNames = new();

            string pathName = Path.Combine(Application.persistentDataPath, "QuickSave", "Mods");
            string[] modFolders = Directory.GetDirectories(pathName);
            foreach (string modFolderPath in modFolders) modNames.Add(Path.GetFileName(modFolderPath));

            return modNames;
        }

        public static void SaveMod(Mod mod) {
            mod.InitMod();

            string modName = Path.Combine("Mods", mod.name, mod.name);
            QuickSaveRaw.Delete(modName + ".json");
            QuickSaveWriter writer = QuickSaveWriter.Create(modName);

            writer.Write("mod", mod);
            writer.Commit();
        }

        public static Mod ReadMod(string modName) {
            modName = Path.Combine("Mods", modName, modName);
            QuickSaveReader reader = QuickSaveReader.Create(modName);
            Debug.Log(modName);
            return reader.Read<Mod>("mod");
        }

        public static void SaveContent(Content content) {
            string contentName = Path.Combine("Base", content.name);

            QuickSaveRaw.Delete(contentName + ".json");
            QuickSaveWriter writer = QuickSaveWriter.Create(contentName);

            content.Wrap();
            writer.Write("type", TypeWrapper.GetString(content.GetType()));
            writer.Write("data", content);
            writer.Commit();
        }

        public static Content ReadContent(string contentName) {
            QuickSaveReader reader = QuickSaveReader.Create(contentName);
            Type type = TypeWrapper.GetSystemType(reader.Read<string>("type"));
            return (Content)reader.Read("data", type);
        }

        public static Content GetContentById(short id) {
            return loadedContents[id];
        }

        public static Content GetContentByName(string name) {
            foreach (Content content in loadedContents.Values) if (content.name == name) return content;
            return null;
        }

        public static T[] GetContentByType<T>() where T : Content{
            List<T> foundMatches = new();
            foreach(Content content in loadedContents.Values) if (TypeEquals(content.GetType(), typeof(T))) foundMatches.Add(content as T);
            return foundMatches.ToArray();
        }

        public static int GetContentCountOfType<T>(bool countHidden = false) where T : Content {
            int count = 0;
            foreach (Content content in loadedContents.Values) if (TypeEquals(content.GetType(), typeof(T)) && (countHidden || !content.hidden)) count++;
            return count;
        }

        public static bool TypeEquals(Type target, Type reference) => target == reference || target.IsSubclassOf(reference);
    }

    [Serializable]
    public class Mod {
        public string name;
        public int version;
        public Wrapper<string> tiles;
        public Wrapper<string> items;
        public Wrapper<string> bullets;
        public Wrapper<string> weapons;
        public Wrapper<string> units;
        public Wrapper<string> blocks;

        public void InitMod() {
            name = "Example Mod";
            version = 1;
            tiles = new Wrapper<string>(new string[1] { "magma-tile" });
            items = new Wrapper<string>(new string[1] { "plastanium" });
            bullets = new Wrapper<string>(new string[1] { "bombBulletType" });
            weapons = new Wrapper<string>(new string[2] { "horizon-weapon", "duo-weapon" });
            units = new Wrapper<string>(new string[1] { "horizon" });
            blocks = new Wrapper<string>(new string[3] { "titanium-wall", "titanium-wall-large", "duo-turret" });
        }

        public void LoadMod() {
            LoadContent(tiles, Path.Combine("Mods", name, "Content", "Tiles"));
            LoadContent(items, Path.Combine("Mods", name, "Content", "Items"));
            LoadContent(bullets, Path.Combine("Mods", name, "Content", "Bullets"));
            LoadContent(weapons, Path.Combine("Mods", name, "Content", "Weapons"));
            LoadContent(units, Path.Combine("Mods", name, "Content", "Units"));
            LoadContent(blocks, Path.Combine("Mods", name, "Content", "Blocks"));

            Debug.Log("Loaded mod:" + name + " with version " + version);
        }

        public void LoadContent(Wrapper<string> content, string path = "") {
            string[] contentNames = content.array;
            foreach (string contentName in contentNames) ContentLoader.ReadContent(Path.Combine(path, contentName));
        }
    }

    [Serializable]
    public abstract class Content {
        public string name;
        public bool hidden = false;
        [JsonIgnore] public short id;
        [JsonIgnore] public Sprite sprite;
        [JsonIgnore] public Sprite spriteFull;

        public Content(string name) {
            this.name = name;

            sprite = AssetLoader.GetSprite(name);
            spriteFull = AssetLoader.GetSprite(name + "-full", true);
            if (!spriteFull) spriteFull = sprite;

            id = (short)ContentLoader.loadedContents.Count;
            ContentLoader.HandleContent(this);
        }

        public virtual void Wrap() { }

        public virtual void UnWrap() { }
    }

    #region - Blocks -

    public class EntityType : Content {
        [JsonIgnore] public Type type;
        public string typeName;

        public ItemStack[] buildCost;

        public float health = 100f, itemMass = -1f;
        public int itemCapacity = 20;
        public bool rotates = false;

        public int maximumFires = 0;
        public bool canGetOnFire = false, canSpreadFire = false;

        public float blinkInterval = 0.5f, blinkOffset = 0f, blinkLength = 1f;

        public EntityType(string name, Type type) : base(name) {
            typeName = TypeWrapper.GetString(type);
            this.type = type;
        }

        public override void Wrap() {
            base.Wrap();
            typeName = TypeWrapper.GetString(type);
        }


        public override void UnWrap() {
            base.UnWrap();
            type = TypeWrapper.GetSystemType(typeName);
        }
    }

    public class BlockType : EntityType {
        [JsonIgnore] public Sprite teamSprite, glowSprite, topSprite, bottomSprite;

        public bool updates = false, breakable = true, solid = true;
        public int size = 1;

        public BlockType(string name, Type type) : base(name, type) {
            teamSprite = AssetLoader.GetSprite(name + "-team", true);
            glowSprite = AssetLoader.GetSprite(name + "-glow", true);
            topSprite = AssetLoader.GetSprite(name + "-top", true);
            bottomSprite = AssetLoader.GetSprite(name + "-bottom", true);
            this.type = type;

            canGetOnFire = false;
            maximumFires = 3;
        }
    }

    public class ItemBlockType : BlockType {
        public ItemBlockType(string name, Type type) : base(name, type) {

        }
    }

    public class DrillBlockType : ItemBlockType {
        [JsonIgnore] public Sprite drillRotorSprite;
        public float drillHardness, drillRate;

        public DrillBlockType(string name, Type type) : base(name, type) {
            drillRotorSprite = AssetLoader.GetSprite(name + "-rotator");
            updates = true;
        }
    }

    public class ConveyorBlockType : ItemBlockType {
        public float itemSpeed = 1f;

        public ConveyorBlockType(string name, Type type) : base(name, type) {
            rotates = true;
            updates = true;
        }
    }

    public class CrafterBlockType : ItemBlockType {
        public CraftPlan craftPlan;

        public CrafterBlockType(string name, Type type) : base(name, type) {
            updates = true;
        }
    }

    public class StorageBlockType : ItemBlockType {
        public StorageBlockType(string name, Type type) : base(name, type) {

        }
    }

    public class TurretBlockType : ItemBlockType {
        public WeaponMount weapon;

        public TurretBlockType(string name, Type type) : base(name, type) {

        }
    }

    public class CoreBlockType : StorageBlockType {
        public CoreBlockType(string name, Type type) : base(name, type) {
            breakable = false;
        }
    }

    public class UnitFactoryBlockType : ItemBlockType {
        public UnitPlan unitPlan;

        public UnitFactoryBlockType(string name, Type type) : base(name, type) {

        }
    }

    public class LandPadBlockType : StorageBlockType {
        [JsonIgnore] public Vector2[] landPositions;
        public Wrapper<Vector2> landPositionsList;

        public int unitCapacity = 0;
        public float unitSize = 1.5f;

        public LandPadBlockType(string name, Type type) : base(name, type) {

        }

        public override void Wrap() {
            base.Wrap();
            landPositionsList = new Wrapper<Vector2>(landPositions);
        }

        public override void UnWrap() {
            base.UnWrap();
            landPositions = landPositionsList.array;
        }
    }



    public class Blocks {
        public const BlockType none = null;
        public static BlockType
            copperWall, copperWallLarge,

            coreShard, container,

            landingPad, landingPadLarge,

            tempest, stinger, path, spread, 
            
            airFactory, 
            
            crafter, graphitePress, siliconSmelter, kiln,
            
            conveyor, 
            
            mechanicalDrill, pneumaticDrill;

        public static void Load() {
            copperWall = new BlockType("copper-wall", typeof(Block)) {
                buildCost = ItemStack.With(Items.copper, 6),

                health = 140
            };

            copperWallLarge = new BlockType("copper-wall-large", typeof(Block)) {
                buildCost = ItemStack.With(Items.copper, 24),

                health = 600,
                size = 2
            };

            coreShard = new CoreBlockType("core-shard", typeof(CoreBlock)) {
                buildCost = ItemStack.With(Items.copper, 1000, Items.lead, 500, Items.titanium, 100),

                hidden = true,
                breakable = false,
                health = 1600,
                size = 3,

                itemCapacity = 1000,

                canGetOnFire = true,
            };

            container = new StorageBlockType("container", typeof(StorageBlock)) {
                buildCost = ItemStack.With(Items.copper, 100, Items.titanium, 25),

                health = 150,
                size = 2,
                itemCapacity = 200,

                canGetOnFire = true,
            };

            landingPad = new LandPadBlockType("landingPad", typeof(LandPadBlock)) {
                buildCost = ItemStack.With(Items.copper, 250, Items.titanium, 75),

                health = 250,
                size = 3,
                solid = false,
                updates = true,
                unitCapacity = 4,
                unitSize = 2.5f,

                landPositions = new Vector2[] {
                    new Vector2(0.8f, 0.8f),
                    new Vector2(0.8f, 2.2f),
                    new Vector2(2.2f, 0.8f),
                    new Vector2(2.2f, 2.2f)
                }
            };

            landingPadLarge = new LandPadBlockType("landingPad-large", typeof(LandPadBlock)) {
                buildCost = ItemStack.With(Items.copper, 250, Items.titanium, 75),

                health = 300,
                size = 3,
                solid = false,
                unitCapacity = 1,
                unitSize = 5f,

                landPositions = new Vector2[] {
                    new Vector2(1.5f, 1.5f)
                }
            };

            tempest = new TurretBlockType("tempest", typeof(TurretBlock)) {
                buildCost = ItemStack.With(Items.copper, 250, Items.titanium, 75),
                weapon = new WeaponMount(Weapons.tempestWeapon, Vector2.zero),

                health = 230f,
                size = 2,

                canGetOnFire = true,
            };

            stinger = new TurretBlockType("stinger", typeof(TurretBlock)) {
                buildCost = ItemStack.With(Items.copper, 250, Items.titanium, 75),
                weapon = new WeaponMount(Weapons.stingerWeapon, Vector2.zero),

                health = 320f,
                size = 2,

                canGetOnFire = true,
            };

            path = new TurretBlockType("path", typeof(TurretBlock)) {
                buildCost = ItemStack.With(Items.copper, 125, Items.graphite, 55, Items.silicon, 35),
                weapon = new WeaponMount(Weapons.pathWeapon, Vector2.zero),

                health = 275f,
                size = 2,

                canGetOnFire = true,
            };

            spread = new TurretBlockType("spread", typeof(TurretBlock)) {
                buildCost = ItemStack.With(Items.copper, 250, Items.titanium, 65, Items.silicon, 80),
                weapon = new WeaponMount(Weapons.spreadWeapon, Vector2.zero),

                health = 245f,
                size = 2,

                canGetOnFire = true,
            };

            airFactory = new UnitFactoryBlockType("air-factory", typeof(UnitFactoryBlock)) {
                buildCost = ItemStack.With(Items.copper, 250, Items.titanium, 75),

                unitPlan = new UnitPlan(Units.flare, 4f, new ItemStack[1] {
                    new ItemStack(Items.silicon, 20)
                }),

                health = 250f,
                size = 3,
                itemCapacity = 50,

                canGetOnFire = true,
            };

            crafter = new CrafterBlockType("crafter", typeof(CrafterBlock)) {
                buildCost = ItemStack.With(Items.copper, 275, Items.lead, 125),
                craftPlan = new CraftPlan() {
                    productStack = new ItemStack(Items.coal, 1),
                    materialList = ItemStack.With(Items.copper, 2),
                    craftTime = 2f
                },

                health = 300f,
                size = 2,
                itemCapacity = 30,

                canGetOnFire = true,
            };

            siliconSmelter = new CrafterBlockType("silicon-smelter", typeof(CrafterBlock)) {
                buildCost = ItemStack.With(Items.copper, 50, Items.lead, 45),

                craftPlan = new CraftPlan() {
                    productStack = new ItemStack(Items.silicon, 1),
                    materialList = ItemStack.With(Items.sand, 2, Items.coal, 1),
                    craftTime = 0.66f
                },

                health = 125,
                size = 2,
                itemCapacity = 30,

                canGetOnFire = true,
            };

            graphitePress = new CrafterBlockType("graphite-press", typeof(CrafterBlock)) {
                buildCost = ItemStack.With(Items.copper, 75, Items.lead, 25),

                craftPlan = new CraftPlan() {
                    productStack = new ItemStack(Items.graphite, 1),
                    materialList = ItemStack.With(Items.coal, 2),
                    craftTime = 1.5f
                },

                health = 95,
                size = 2,
                itemCapacity = 10,

                canGetOnFire = true,
            };

            kiln = new CrafterBlockType("kiln", typeof(CrafterBlock)) {
                buildCost = ItemStack.With(Items.copper, 100, Items.lead, 35, Items.graphite, 15),

                craftPlan = new CraftPlan() {
                    productStack = new ItemStack(Items.metaglass, 1),
                    materialList = ItemStack.With(Items.sand, 1, Items.lead, 1),
                    craftTime = 0.5f
                },

                health = 120,
                size = 2,
                itemCapacity = 16,

                canGetOnFire = true,
            };

            conveyor = new ConveyorBlockType("conveyor", typeof(ConveyorBlock)) {
                buildCost = ItemStack.With(Items.copper, 2),
                health = 75f,
                size = 1,
                itemCapacity = 3,
                itemSpeed = 4f,
                rotates = true,
            };

            mechanicalDrill = new DrillBlockType("mechanical-drill", typeof(DrillBlock)) {
                buildCost = ItemStack.With(Items.copper, 16),
                health = 100f,
                size = 2,
                itemCapacity = 10,

                drillHardness = 2.5f,
                drillRate = 1f,

                canGetOnFire = true,
            };

            pneumaticDrill = new DrillBlockType("pneumatic-drill", typeof(DrillBlock)) {
                buildCost = ItemStack.With(Items.copper, 24, Items.graphite, 10),
                health = 175f,
                size = 2,
                itemCapacity = 12,

                drillHardness = 3.5f,
                drillRate = 1.75f,

                canGetOnFire = true,
            };
        }
    }

    #endregion

    #region - Units -
    [Serializable]
    public class UnitType : EntityType {
        [JsonIgnore] public Sprite cellSprite, outlineSprite;

        public float size = 1.5f;
        public float velocityCap = 2f, drag = 1f, bankAmount = 25f, bankSpeed = 5f, rotationSpeed = 90f;
        public bool useAerodynamics = true;

        public float range = 15f, fov = 95;

        public float fuelCapacity = 60f, fuelConsumption = 1.5f, fuelRefillRate = 7.5f;

        public float flyHeight = 18f;

        public float force = 500f;
        public float emptyMass = 10f, fuelMass = 3.5f;

        public WeaponMount[] weapons = new WeaponMount[0];

        public UnitType(string name, Type type) : base(name, type) {
            cellSprite = AssetLoader.GetSprite(name + "-cell");
            outlineSprite = AssetLoader.GetSprite(name + "-outline");
            typeName = TypeWrapper.GetString(type);
            this.type = type;
            rotates = true;

            canGetOnFire = true;
            maximumFires = 2;
        }
    }

    public class CoreUnitType : UnitType {
        public float itemPickupDistance = 3f, buildSpeedMultiplier = 1f;

        public CoreUnitType(string name, Type type) : base(name, type) {

        }
    }

    public class Units {
        public const UnitType none = null;
        public static UnitType flare, horizon, zenith, pulse, poly;

        public static void Load() {
            flare = new UnitType("flare", typeof(Unit)) {
                weapons = new WeaponMount[1] {
                    new WeaponMount(Weapons.flareWeapon, new Vector2(-0.25f, 0.3f), true),
                    //new WeaponMount(Weapons.missileRack, new Vector2(0f, 1f), false)
                },

                useAerodynamics = true,

                health = 75f,
                size = 1.5f, 
                velocityCap = 20f,
                drag = 0.1f,

                rotationSpeed = 160f,
                bankAmount = 30f,

                range = 15f,
                fov = 100f,
                flyHeight = 18f,

                fuelCapacity = 120f,
                fuelConsumption = 1.25f,
                fuelRefillRate = 8.25f,

                force = 500f,
                emptyMass = 10f,
                itemMass = 3f,
                fuelMass = 3.5f,
            };

            horizon = new UnitType("horizon", typeof(Unit)) {
                weapons = new WeaponMount[1] {
                    new WeaponMount(Weapons.smallAutoWeapon, new Vector2(0.43675f, 0.15f), true),
                },

                useAerodynamics = true,

                health = 215f,
                size = 2.25f,
                velocityCap = 10f,
                itemCapacity = 25,
                drag = 0.2f,

                rotationSpeed = 100f,
                bankAmount = 40f,

                range = 5f,
                fov = 110f,
                flyHeight = 12f,

                fuelCapacity = 240f,
                fuelConsumption = 2.15f,
                fuelRefillRate = 14.5f,

                force = 800f,
                emptyMass = 15.5f,
                itemMass = 10f,
                fuelMass = 5f,
            };

            zenith = new UnitType("zenith", typeof(Unit)) {
                weapons = new WeaponMount[1] {
                    new WeaponMount(Weapons.zenithMissiles, new Vector2(0.8f, -0.15f), true, true),
                },

                useAerodynamics = false,

                health = 825f,
                size = 3.5f,
                velocityCap = 7.5f,
                itemCapacity = 50,
                drag = 0.5f,

                rotationSpeed = 70f,
                bankAmount = 20f,

                range = 15f,
                fov = 90f,
                flyHeight = 12f,

                fuelCapacity = 325f,
                fuelConsumption = 2.25f,
                fuelRefillRate = 20.75f,

                force = 1030f,
                emptyMass = 25.25f,
                itemMass = 6.5f,
                fuelMass = 10f,

                maximumFires = 3,
            };

            pulse = new UnitType("pulse", typeof(Unit)) {
                weapons = new WeaponMount[1] {
                    new WeaponMount(Weapons.smallAutoWeapon, new Vector2(0.43675f, 0.15f), true),
                },

                useAerodynamics = false,

                health = 250f,
                velocityCap = 2f,
                drag = 1.5f,
                rotationSpeed = 100f,
                bankAmount = 40f,
                range = 22.5f,
                fov = 120f,

                fuelCapacity = 90f,
                fuelConsumption = 1.5f,
                fuelRefillRate = 7.5f
            };

            poly = new CoreUnitType("poly", typeof(CoreUnit)) {
                useAerodynamics = false,

                health = 255f,
                size = 1.875f,
                velocityCap = 9f,
                itemCapacity = 120,
                drag = 1f,

                rotationSpeed = 120f,
                bankAmount = 10f,

                range = 10f,
                fov = 180f,
                flyHeight = 9f,

                fuelCapacity = 580f,
                fuelConsumption = 0.22f,
                fuelRefillRate = 23.5f,

                force = 865f,
                emptyMass = 5.5f,
                itemMass = 10.5f,
                fuelMass = 17f,

                buildSpeedMultiplier = 1f,
                itemPickupDistance = 6f,
            };
        }
    }

    #endregion

    #region - Weapons -
    [Serializable]
    public class WeaponType : Content {
        [JsonIgnore] public Type type;
        [JsonIgnore] public Item ammoItem;
        [JsonIgnore] public Sprite outlineSprite;
        [JsonIgnore] public Animation[] animations;
        [JsonIgnore] public WeaponBarrel[] barrels;

        public Vector2 shootOffset = Vector2.zero;
        public BulletType bulletType;

        public string typeName;
        private string ammoItemName;
        private Wrapper<Animation> animationWrapper;
        private Wrapper<WeaponBarrel> barrelWrapper;

        public bool isIndependent = false;
        public bool consumesItems = false;
        public bool predictTarget = true;

        public int clipSize = 10;
        public float maxTargetDeviation = 15f, spread = 0.05f, recoil = 0.75f, returnSpeed = 1f, shootTime = 1f, reloadTime = 1f, rotateSpeed = 90f;

        public WeaponType(string name, Type type) : base(name) {
            typeName = TypeWrapper.GetString(type);
            outlineSprite = AssetLoader.GetSprite(name + "-outline", true);
            this.type = type;
        }

        public WeaponType(string name, Type type, Item ammoItem) : base(name) {
            typeName = TypeWrapper.GetString(type);
            outlineSprite = AssetLoader.GetSprite(name + "-outline", true);
            ammoItemName = ammoItem.name;
            this.ammoItem = ammoItem;
            this.type = type;
        }

        public float Range { get => bulletType.velocity * bulletType.lifeTime; }

        public override void Wrap() {
            base.Wrap();
            typeName = TypeWrapper.GetString(type);
            ammoItemName = ammoItem == null ? "empty" : ammoItem.name;
            animationWrapper = new Wrapper<Animation>(animations);
            barrelWrapper = new Wrapper<WeaponBarrel>(barrels);
        }

        public override void UnWrap() {
            base.UnWrap();
            type = TypeWrapper.GetSystemType(typeName);
            ammoItem = ammoItemName == "empty" ? null : ContentLoader.GetContentByName(ammoItemName) as Item;
            animations = animationWrapper.array;
            barrels = barrelWrapper.array;
        }
    }

    public class Weapons {
        public const Weapon none = null;

        // Base weapons
        public static WeaponType smallAutoWeapon, flareWeapon, tempestWeapon, stingerWeapon, pathWeapon, spreadWeapon;

        //Unit weapons
        public static WeaponType zenithMissiles;

        // Item related weapons 
        public static WeaponType missileRack;

        public static void Load() {

            smallAutoWeapon = new WeaponType("small-auto-weapon", typeof(RaycastWeapon)) {
                bulletType = Bullets.basicBulletType,
                shootOffset = new Vector2(0, 0.37f),
                recoil = 0f,
                clipSize = 25,
                shootTime = 0.15f,
                reloadTime = 5f
            };

            flareWeapon = new WeaponType("flare-weapon", typeof(RaycastWeapon)) {
                bulletType = Bullets.basicBulletType,
                shootOffset = new Vector2(0, 0.37f),
                recoil = 0.075f,
                returnSpeed = 3f,
                clipSize = 25,
                shootTime = 0.15f,
                reloadTime = 5f
            };

            zenithMissiles = new WeaponType("zenith-missiles", typeof(RaycastWeapon)) {
                bulletType = new BulletType("zenith-missile") {
                    bulletClass = BulletClass.instant,
                    damage = 7.5f,
                    lifeTime = 0.5f,
                    velocity = 100f
                },

                shootOffset = new Vector2(0, 0.25f),

                isIndependent = true,
                recoil = 0.05f,
                returnSpeed = 2f,
                clipSize = 10,
                shootTime = 0.2f,
                reloadTime = 3.5f,
                rotateSpeed = 115f
            };

            tempestWeapon = new WeaponType("tempest-weapon", typeof(RaycastWeapon)) {
                bulletType = Bullets.instantBulletType,
                shootOffset = new Vector2(0, 0.5f),

                isIndependent = true,
                recoil = 0.1f,
                returnSpeed = 2f,
                clipSize = 12,
                shootTime = 0.075f,
                reloadTime = 2f,
                rotateSpeed = 90f
            };

            stingerWeapon = new WeaponType("stinger-weapon", typeof(RaycastWeapon)) {
                bulletType = new BulletType("stinger-bullet") {
                    bulletClass = BulletClass.instant,
                    damage = 15f,
                    lifeTime = 0.5f,
                    velocity = 200f
                },
                shootOffset = new Vector2(0, 0.5f),

                isIndependent = true,
                recoil = 0.2f,
                clipSize = 5,
                shootTime = 0.3f,
                reloadTime = 4f,
                rotateSpeed = 60f
            };

            pathWeapon = new WeaponType("path-weapon", typeof(RaycastWeapon)) {
                bulletType = new BulletType("path-bullet") {
                    bulletClass = BulletClass.instant,
                    damage = 3f,
                    lifeTime = 0.35f,
                    velocity = 150f
                },
                shootOffset = new Vector2(0, 0.75f),

                isIndependent = true,
                animations = new Animation[1] { new Animation("-belt", 3, Animation.Case.Shoot) },
                recoil = 0.02f,
                clipSize = 50,
                shootTime = 0.03f,
                reloadTime = 6f,
                rotateSpeed = 120f
            };

            spreadWeapon = new WeaponType("spread-weapon", typeof(RaycastWeapon)) {
                bulletType = Bullets.basicBulletType,
                shootOffset = Vector2.zero,

                barrels = new WeaponBarrel[4] {
                    new WeaponBarrel("spread-weapon", 1, new Vector2(-0.6f, 0.75f)),
                    new WeaponBarrel("spread-weapon", 4, new Vector2(0.6f, 0.75f)),
                    new WeaponBarrel("spread-weapon", 2, new Vector2(-0.4f, 0.55f)),
                    new WeaponBarrel("spread-weapon", 3, new Vector2(0.4f, 0.55f)),
                },

                isIndependent = true,
                recoil = 0.1f,
                clipSize = 20,
                shootTime = 0.085f,
                reloadTime = 6f,
                rotateSpeed = 100f,
            };

            missileRack = new WeaponType("missileRack", typeof(KineticWeapon), Items.missileX1) {
                bulletType = Bullets.missileBulletType,
                shootOffset = new Vector2(0, 0.5f),

                isIndependent = true,
                consumesItems = true,
                maxTargetDeviation = 360f,

                clipSize = 1,
                reloadTime = 5f,
                rotateSpeed = 0f
            };
        }
    }

    [Serializable]
    public class BulletType : Content {
        public BulletClass bulletClass;
        public float damage = 10f, buildingDamageMultiplier = 1f, velocity = 100f, lifeTime = 1f;
        public float blastRadius = -1f, minimumBlastDamage = 0f;
        public float homingStrength = 30f;

        public BulletType(string name) : base(name) {

        }
    }

    public class Bullets {
        public const BulletType none = null;
        public static BulletType basicBulletType, instantBulletType, missileBulletType;

        public static void Load() {
            basicBulletType = new BulletType("BasicBulletType") {
                bulletClass = BulletClass.instant,
                damage = 7.5f,
                lifeTime = 0.35f,
                buildingDamageMultiplier = 2f,
                velocity = 90f
            };

            instantBulletType = new BulletType("InstantBulletType") {
                bulletClass = BulletClass.instant,
                damage = 2.5f,
                lifeTime = 0.5f,
                buildingDamageMultiplier = 2f,
                velocity = 100f
            };

            missileBulletType = new BulletType("bombBulletType") {
                bulletClass = BulletClass.homingPhysical,
                damage = 100f,
                minimumBlastDamage = 25f,
                blastRadius = 1f,
                buildingDamageMultiplier = 2f,
                velocity = 25f,
                lifeTime = 5f,
                homingStrength = 120f,
            };
        }
    }

    #endregion

    #region - Map -

    [Serializable]
    public class TileType : Content {
        [JsonIgnore] public TileBase tile;
        [JsonIgnore] public TileBase[] allVariants;
        [JsonIgnore] public Item itemDrop;
        public Color color;

        public int variants;
        public bool allowBuildings = true, flammable = false, isWater = false;
        public string itemDropName;

        public TileType(string name, int variants = 1) : base(name) {
            this.variants = variants;

            if (this.variants < 1) this.variants = 1;

            if (this.variants == 1) {
                tile = AssetLoader.GetAsset<TileBase>(name);
            } else {
                allVariants = new TileBase[variants];
                for (int i = 0; i < this.variants; i++) allVariants[i] = AssetLoader.GetAsset<TileBase>(name + (i + 1)); 
                tile = allVariants[0];
            }

            color = sprite.texture.GetPixel(sprite.texture.width / 2, sprite.texture.height / 2);
        }

        public TileType(string name, int variants, Item itemDrop) : base(name) {
            this.itemDrop = itemDrop;
            itemDropName = itemDrop.name;

            this.variants = variants;

            if (this.variants < 1) this.variants = 1;

            if (this.variants == 1) {
                tile = AssetLoader.GetAsset<TileBase>(name);
            } else {
                allVariants = new TileBase[variants];
                for (int i = 0; i < this.variants; i++) allVariants[i] = AssetLoader.GetAsset<TileBase>(name + (i + 1));

                tile = allVariants[0];
            }

            float mult = id / 30f;
            color = new Color(mult, mult, mult);
        }

        public virtual TileBase[] GetAllTiles() {
            if (variants == 1) return new TileBase[1] { tile };
            else return allVariants;
        }

        public virtual TileBase GetRandomTileVariant() {
            if (variants == 1) return tile;
            return allVariants[Random.Range(0, variants - 1)];
        }

        public override void Wrap() {
            base.Wrap();
            itemDropName = itemDrop == null ? "empty" : itemDrop.name;
        }

        public override void UnWrap() {
            base.UnWrap();
            itemDrop = itemDropName == "empty" ? null : ContentLoader.GetContentByName(itemDropName) as Item;
        }
    }

    [Serializable]
    public class OreTileType : TileType {
        public float oreThreshold, oreScale;

        public OreTileType(string name, int variants, Item itemDrop) : base(name, variants, itemDrop) {

        }
    }

    public class Tiles {
        public const TileType none = null;
        //Base tiles
        public static TileType darksandWater, darksand, deepWater, grass, ice, metalFloor, metalFloor2, metalFloorWarning, metalFloorDamaged, sandFloor, sandWater, shale, snow, stone, water;

        //Ore tiles
        public static TileType copperOre, leadOre, titaniumOre, coalOre, thoriumOre;

        //Miscelaneous Tiles
        public static TileType blockTile;

        public static void Load() {
            darksandWater = new TileType("darksand-water") {
                isWater = true,
            };

            darksand = new TileType("darksand", 3, Items.sand);

            deepWater = new TileType("deep-water") {
                allowBuildings = false,
                isWater = true,
            };

            grass = new TileType("grass", 3);

            ice = new TileType("ice", 3);

            metalFloor = new TileType("metal-floor");

            metalFloor2 = new TileType("metal-floor-2");

            metalFloorWarning = new TileType("metal-floor-warning");

            metalFloorDamaged = new TileType("metal-floor-damaged", 3);

            sandFloor = new TileType("sand-floor", 3, Items.sand);

            sandWater = new TileType("sand-water") {
                isWater = true,
            };

            shale = new TileType("shale", 3);

            snow = new TileType("snow", 3);

            stone = new TileType("stone", 3);

            water = new TileType("water") {
                allowBuildings = false,
                isWater = true,
            };

            copperOre = new OreTileType("ore-copper", 3, Items.copper);

            leadOre = new OreTileType("ore-lead", 3, Items.lead);

            titaniumOre = new OreTileType("ore-titanium", 3, Items.titanium);

            coalOre = new OreTileType("ore-coal", 3, Items.coal);

            thoriumOre = new OreTileType("ore-thorium", 3, Items.thorium);

            blockTile = new TileType("blockTile");
        }
    }

    public class MapType : Content{
        public Dictionary<int, TileType> tileReferences = new Dictionary<int, TileType>();

        public Wrapper2D<int> wrapper2D;
        public int[,] tileMapIDs;

        public MapType(string name, Wrapper2D<int> wrapper2D) : base(name) {
            this.wrapper2D = wrapper2D;
            this.wrapper2D.array2D.CopyTo(tileMapIDs, 0);
        }

        public TileType GetTileTypeAt(Vector2Int position) {
            int id = tileMapIDs[position.x, position.y];
            return tileReferences[id];
        }
    }

    #endregion

    #region - Items -

    [Serializable]
    public class Item : Content {
        public Color color;
        public float explosiveness = 0, flammability = 0, radioactivity = 0, charge = 0;
        public float hardness = 0, cost = 1;

        // Mass in tons per item piece
        public float mass = 0.01f;

        public bool lowPriority = false, buildable = true;

        public Item(string name) : base(name) {

        }
    }

    public class Items {
        public static Item copper, lead, titanium, coal, graphite, metaglass, sand, silicon, thorium;
        public static Item basicAmmo, missileX1;

        public static void Load() {
            copper = new Item("copper") {
                color = new Color(0xD9, 0x9D, 0x73),
                hardness = 1,
                cost = 0.5f
            };

            lead = new Item("lead") {
                color = new Color(0x8c, 0x7f, 0xa9),
                hardness = 1,
                cost = 0.7f
            };

            metaglass = new Item("metaglass") {
                color = new Color(0xeb, 0xee, 0xf5),
                cost = 1.5f
            };

            graphite = new Item("graphite") {
                color = new Color(0xb2, 0xc6, 0xd2),
                cost = 1f
            };

            sand = new Item("sand") {
                color = new Color(0xf7, 0xcb, 0xa4),
                lowPriority = true,
                buildable = false
            };

            coal = new Item("coal") {
                color = new Color(0x27, 0x27, 0x27),
                explosiveness = 0.2f,
                flammability = 1f,
                hardness = 2,
                buildable = false
            };

            titanium = new Item("titanium") {
                color = new Color(0x8d, 0xa1, 0xe3),
                hardness = 3,
                cost = 1f
            };

            thorium = new Item("thorium") {
                color = new Color(0xf9, 0xa3, 0xc7),
                explosiveness = 0.2f,
                hardness = 4,
                radioactivity = 1f,
                cost = 1.1f
            };

            silicon = new Item("silicon") {
                color = new Color(0x53, 0x56, 0x5c),
                cost = 0.8f
            };

            basicAmmo = new Item("basicAmmo") {
                color = new Color(0xD9, 0x9D, 0x73),
                flammability = 0.1f,
                buildable = false
            };

            missileX1 = new Item("missileX1") {
                color = new Color(0x53, 0x56, 0x5c),
                explosiveness = 0.75f,
                flammability = 0.2f,
                charge = 0.1f,
                cost = 5,
                buildable = false
            };
        }
    }

    #endregion

    #region - Structures - 

    public enum BulletClass {
        instant,          //Laser, raycast bulletType
        constantPhysical, //Basic bullet, speed keeps constant
        slowDownPhysical, //Grenade or similar, speed is afected by drag
        homingPhysical    //Missile bullet type, once fired it's fully autonomous
    }

    /// <summary>
    /// Stores the amount of a defined item
    /// </summary>
    [Serializable]
    public class ItemStack {
        [JsonIgnore]
        public Item item;

        public string itemName;
        public int amount;

        public ItemStack() {
            item = Items.copper;
        }

        public ItemStack(Item item, int amount = 0) {
            if (item == null) item = Items.copper;
            this.item = item;
            this.amount = amount;

            itemName = item.name;
        }

        public ItemStack Set(Item item, int amount) {
            this.item = item;
            this.amount = amount;
            return this;
        }

        public ItemStack Copy() {
            return new ItemStack(item, amount);
        }

        public bool Equals(ItemStack other) {
            return other != null && other.item == item && other.amount == amount;
        }

        public static Item[] ToItems(ItemStack[] stacks) {
            Item[] items = new Item[stacks.Length];
            for(int i = 0; i < stacks.Length; i++) items[i] = stacks[i].item;
            return items;
        }

        public static ItemStack[] Multiply(ItemStack[] stacks, float amount) {
            ItemStack[] copy = new ItemStack[stacks.Length];
            for (int i = 0; i < copy.Length; i++) {
                copy[i] = new ItemStack(stacks[i].item, Mathf.RoundToInt(stacks[i].amount * amount));
            }
            return copy;
        }

        public static ItemStack[] With(params object[] items) {
            ItemStack[] stacks = new ItemStack[items.Length / 2];
            for (int i = 0; i < items.Length; i += 2) {
                stacks[i / 2] = new ItemStack((Item)items[i], (int)items[i + 1]);
            }
            return stacks;
        }

        public static int[] Serialize(ItemStack[] stacks) {
            int[] serializedArray = new int[stacks.Length * 2];
            for (int i = 0; i < serializedArray.Length; i += 2) {
                serializedArray[i] = stacks[i].item.id;
                serializedArray[i + 1] = stacks[i].amount;
            }
            return serializedArray;
        }

        public static ItemStack[] DeSerialize(int[] serializedArray) {
            ItemStack[] stacks = new ItemStack[serializedArray.Length / 2];
            for (int i = 0; i < serializedArray.Length; i += 2) {
                stacks[i / 2] = new ItemStack((Item)ContentLoader.GetContentById((short)serializedArray[i]), (int)serializedArray[i + 1]);
            }
            return stacks;
        }

        public static List<ItemStack> List(params object[] items) {
            List<ItemStack> stacks = new(items.Length / 2);
            for (int i = 0; i < items.Length; i += 2) {
                stacks.Add(new ItemStack((Item)items[i], (int)items[i + 1]));
            }
            return stacks;
        }
    }

    public class Inventory {
        public event EventHandler OnAmountChanged;

        public Dictionary<Item, int> items;
        public Item[] allowedItems;

        public int amountCap;
        public float maxMass;

        public Inventory(int amountCap = -1, float maxMass = -1f, Item[] allowedItems = null) {
            items = new Dictionary<Item, int>();
            this.amountCap = amountCap;
            this.allowedItems = allowedItems;
        }

        public void Clear() {
            items = new Dictionary<Item, int>();
        }

        public ItemStack[] ToArray() {
            ItemStack[] stacks = new ItemStack[items.Count];
            int i = 0;

            foreach(KeyValuePair<Item, int> valuePair in items) {
                stacks[i] = new(valuePair.Key, valuePair.Value);
                i++;
            }
        
            return stacks;
        }

        public Item[] ToItems() {
            return items.Keys.ToArray();
        }

        public void SetAllowedItems(Item[] allowedItems) {
            this.allowedItems = allowedItems;
        }

        public Item First() {
            foreach (Item item in items.Keys) if (items[item] != 0) return item;
            return null;
        }

        public Item First(Item[] filter) {
            foreach (Item item in items.Keys) if (filter.Contains(item) && items[item] != 0) return item;
            return null;
        }

        public bool Empty() {
            foreach (int amount in items.Values) if (amount != 0) return false;   
            return true;
        }

        public bool Empty(Item[] filter) {
            foreach (Item item in items.Keys) if (filter.Contains(item) && items[item] != 0) return false;
            return true;
        }

        public bool Contains(Item item) {
            return items.ContainsKey(item);
        }

        public bool Has(Item item, int amount) {
            if (!Contains(item)) return false;
            else return items[item] >= amount;
        }

        public bool Has(ItemStack stack) {
            return Has(stack.item, stack.amount);
        }

        public bool Has(ItemStack[] stacks) {
            foreach(ItemStack itemStack in stacks) if (!Has(itemStack)) return false;    
            return true;
        }

        public bool HasToMax(Item item, int amount) {
            if (!Contains(item)) return false;
            else return items[item] >= amount || items[item] == amountCap;
        }

        public bool HasToMax(ItemStack stack) {
            return HasToMax(stack.item, stack.amount);
        }

        public bool HasToMax(ItemStack[] stacks) {
            foreach (ItemStack itemStack in stacks) if (!HasToMax(itemStack)) return false;
            return true;
        }

        public bool Allowed(Item item) {
            return allowedItems == null || allowedItems.Contains(item);
        }

        public bool Allowed(Item[] items) {
            foreach (Item item in items) if (!Allowed(item)) return false;
            return true;
        }

        public bool Empty(Item item) {
            return Contains(item) && items[item] == 0;
        }

        public bool Full(Item item) {
            return amountCap != -1f && Has(item, amountCap);
        }

        public int AmountToFull(Item item) {
            if (!Contains(item)) return amountCap;
            return amountCap - items[item];
        }

        public int Add(Item item, int amount, bool update = true) {
            if (Full(item) || amount == 0) return amount;
            if (!Contains(item)) items.Add(item, 0);

            int amountToReturn = amountCap == -1 ? 0 : Mathf.Clamp(items[item] + amount - amountCap, 0, amount);
            items[item] += amount - amountToReturn;

            if (update) AmountChanged();
            return amountToReturn;
        }

        public int Add(ItemStack stack, bool update = true) {
            int value = Add(stack.item, stack.amount, false);
            if (update) AmountChanged();
            return value;
        }

        public void Add(ItemStack[] stacks) {
            foreach (ItemStack itemStack in stacks) Add(itemStack, false);
            AmountChanged();
        }

        public int Max(Item item) {
            if (!Contains(item)) {
                if (!Allowed(item)) return 0;
                else return amountCap;
            }
            return amountCap - items[item];
        }

        public int[] Max(Item[] items) {
            int[] maxItems = new int[items.Length];
            for (int i = 0; i < items.Length; i++) maxItems[i] = Max(items[i]);
            return maxItems;
        }

        public bool Fits(Item item, int amount) {
            return amount <= Max(item);
        }

        public bool Fits(ItemStack stack) {
            return Fits(stack.item, stack.amount);
        }

        public bool Fits(ItemStack[] stacks) {
            foreach (ItemStack stack in stacks) if (!Fits(stack)) return false;
            return true;
        }

        public int Substract(Item item, int amount, bool update = true) {
            if (!Contains(item) || Empty(item) || amount == 0) return amount;

            int amountToReturn = Mathf.Clamp(amount - items[item], 0, amount);
            items[item] -= amount - amountToReturn;

            if (items[item] == 0) items.Remove(item);

            if (update) AmountChanged();
            return amountToReturn;
        }

        public int Substract(ItemStack itemStack, bool update = true) {
            int value = Substract(itemStack.item, itemStack.amount, false);
            if (update) AmountChanged();
            return value;
        }

        public void Substract(ItemStack[] itemStacks) {
            foreach (ItemStack itemStack in itemStacks) Substract(itemStack, false);
            AmountChanged();
        }

        public int MaxTransferAmount(Inventory other, Item item) {
            if (!Contains(item) || !other.Allowed(item)) return 0;
            return Mathf.Min(items[item], other.Max(item));
        }

        public int MaxTransferAmount(Inventory other, ItemStack stack) {
            if (!Contains(stack.item) || !other.Allowed(stack.item)) return 0;
            return Mathf.Min(stack.amount, items[stack.item], other.Max(stack.item));
        }

        public int MaxTransferSubstractAmount(Inventory other, ItemStack stack) {
            if (!other.Contains(stack.item) || !Contains(stack.item)) return 0;
            return Mathf.Min(stack.amount, items[stack.item], other.items[stack.item]);
        }

        public void TransferAll(Inventory other) {
            ItemStack[] stacksToSend = ToArray();
        
            for(int i = 0; i < stacksToSend.Length; i++) {
                ItemStack stack = stacksToSend[i];
                stack.amount = MaxTransferAmount(other, stack.item);
            }

            Transfer(other, stacksToSend);
        }

        public void TransferAll(Inventory other, Item[] filter) {
            ItemStack[] stacksToSend = new ItemStack[filter.Length];

            for (int i = 0; i < filter.Length; i++) {
                Item item = filter[i];
                int amountToSend = MaxTransferAmount(other, item);
                stacksToSend[i] = new ItemStack(item, amountToSend);
            }

            Transfer(other, stacksToSend);
        }

        public void TransferAmount(Inventory other, ItemStack[] stacks) {
            ItemStack[] stacksToSend = new ItemStack[stacks.Length];

            for (int i = 0; i < stacks.Length; i++) {
                ItemStack stack = stacks[i];
                int amountToSend = MaxTransferAmount(other, stack);
                stacksToSend[i] = new ItemStack(stack.item, amountToSend);
            }

            Transfer(other, stacksToSend);
        }

        public void TransferSubstractAmount(Inventory other, ItemStack[] stacks) {
            ItemStack[] stacksToSend = new ItemStack[stacks.Length];

            for (int i = 0; i < stacks.Length; i++) {
                ItemStack stack = stacks[i];
                int amountToSend = MaxTransferSubstractAmount(other, stack);
                stacksToSend[i] = new ItemStack(stack.item, amountToSend);
            }

            TransferSubstract(other, stacksToSend);
        }

        public void Transfer(Inventory other, ItemStack[] stacks) {
            Substract(stacks);
            other.Add(stacks);
        }

        public void TransferSubstract(Inventory other, ItemStack[] stacks) {
            Substract(stacks);
            other.Substract(stacks);
        }

        public void AmountChanged() {
            OnAmountChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public struct SerializableItemList {
        public ItemStack[] itemStacks;
        public int maxCapacity;
        public float maxMass;

        public SerializableItemList(Dictionary<Item, ItemStack> itemStacks, int maxCapacity, float maxMass) {
            this.itemStacks = new ItemStack[itemStacks.Count];
            itemStacks.Values.CopyTo(this.itemStacks, 0);
            this.maxCapacity = maxCapacity;
            this.maxMass = maxMass;
        }
    }

    public struct WeaponMount {
        [JsonIgnore] public WeaponType weapon;
        public string weaponName;
        public Vector2 position;
        public bool mirrored;
        public bool onTop;

        public WeaponMount(WeaponType weapon, Vector2 position, bool mirrored = false, bool onTop = false) {
            this.weapon = weapon;
            this.weaponName = weapon.name;
            this.position = position;
            this.mirrored = mirrored;
            this.onTop = onTop;
        }
    }

    public struct WeaponBarrel {
        [JsonIgnore] public Sprite barrelSprite;
        [JsonIgnore] public Sprite barrelOutlineSprite;
        public Vector2 shootOffset;

        public WeaponBarrel(string name, int barrelNum, Vector2 shootOffset) {
            barrelSprite = AssetLoader.GetSprite(name + "-barrel" + barrelNum);
            barrelOutlineSprite = AssetLoader.GetSprite(name + "-barrel" + "-outline" + barrelNum);
            this.shootOffset = shootOffset;
        }
    }

    public struct UnitPlan {
        public string unitName;
        public float craftTime;
        public ItemStack[] materialList;

        public UnitPlan(UnitType unit, float craftTime, ItemStack[] materialList) {
            this.unitName = unit.name;
            this.materialList = materialList;
            this.craftTime = craftTime;
        }

        public UnitType GetUnit() => (UnitType)ContentLoader.GetContentByName(unitName);
    }

    public struct CraftPlan {
        public ItemStack productStack;
        public float craftTime;
        public ItemStack[] materialList;

        public CraftPlan(ItemStack productStack, float craftTime, ItemStack[] materialList) {
            this.productStack = productStack;
            this.materialList = materialList;
            this.craftTime = craftTime;
        }
    }

    #endregion
}

namespace Frontiers.Content.Maps {
    public class MapLoader {
        public const int TilesPerString = 1000;

        public static event EventHandler<MapLoadedEvent> OnMapLoaded;
        public class MapLoadedEvent {
            public Map loadedMap;
        }

        public static void ReciveMap(string name, Vector2 size, string[] tileData) {
            MapData mapData = CreateMap(Vector2Int.CeilToInt(size), tileData);
            Map map = new(name, mapData);
            OnMapLoaded?.Invoke(null, new MapLoadedEvent() { loadedMap = map });
        }

        public static void LoadMap(string name) {
            MapData mapData = ReadMap(name);
            Map map = new(name, mapData);
            OnMapLoaded?.Invoke(null, new MapLoadedEvent() { loadedMap = map });
        }

        public static void SaveMap(Map map) {
            StoreMap(map.name, map.GetMapData());
        }

        public static void StoreMap(string name, MapData mapData) {
            string mapName = Path.Combine("Maps", name);
            QuickSaveRaw.Delete(mapName + ".json");
            QuickSaveWriter writer = QuickSaveWriter.Create(mapName);

            writer.Write("data", mapData);
            writer.Commit();
        }

        public static MapData ReadMap(string name) {
            name = Path.Combine("Maps", name);
            QuickSaveReader reader = QuickSaveReader.Create(name);

            return reader.Read<MapData>("data");
        }

        public static MapData CreateMap(Vector2Int size, string[] tileData) {
            return new MapData(size, tileData);
        }
    }

    public class Map {
        public string name;

        public List<Entity> loadedEntities = new();
        public Dictionary<TileBase, TileType> tileTypeDictionary = new();

        public Tilemap groundTilemap;
        public Tilemap oreTilemap;
        public Tilemap solidTilemap;

        public Tilemap[] tilemaps;

        public List<Block> blocks = new();
        public List<Unit> units = new();

        public MapData mapData;

        public Vector2Int size;
        public bool loaded;

        public enum MapLayer {
            Ground = 0,
            Ore = 1,
            Solid = 2,
            Total = 3
        }

        public Map(string name, int width, int height, Tilemap[] tilemaps) {
            this.name = name;
            this.tilemaps = tilemaps;
            size = new(width, height);

            LoadTileTypeDictionary();

            loaded = true;
        }

        public Map(string name, int width, int height) {
            this.name = name;
            size = new(width, height);

            LoadTilemapReferences();
            LoadTileTypeDictionary();

            loaded = true;
        }

        public Map(string name, MapData mapData) {
            this.name = name;
            this.mapData = mapData;
            size = mapData.size;

            LoadTilemapReferences();
            LoadTileTypeDictionary();
            LoadTilemapData(mapData.tilemapData);

            loaded = true;
        }

        public void LoadTilemapReferences() {
            tilemaps = new Tilemap[(int)MapLayer.Total];
            for(int i = 0; i < (int)MapLayer.Total; i++) {
                tilemaps[i] = MapManager.Instance.tilemaps[i];
            }
        }

        public void LoadTilemapData(MapData.TilemapData tilemapData) {
            int layers = (int)MapLayer.Total;
            string[,,] tileNameArray = tilemapData.DecodeThis();

            for (int z = 0; z < layers; z++) {
                MapLayer layer = (MapLayer)z;

                for (int x = 0; x < size.x; x++) {
                    for (int y = 0; y < size.y; y++) {
                        if (tileNameArray[x, y, z] == null) continue;
                        TileBase tile = GetTileType(tileNameArray[x, y, z]).GetRandomTileVariant();
                        PlaceTile(layer, new Vector2Int(x, y), tile);
                    }
                }
            }
        }

        public void LoadTileTypeDictionary() {
            foreach (TileType tileType in ContentLoader.GetContentByType<TileType>()) {
                foreach (TileBase tileBase in tileType.GetAllTiles()) {
                    tileTypeDictionary.Add(tileBase, tileType);
                }
            }
        }

        public void Save() {
            mapData = new MapData(this);
        }

        public MapData GetMapData() {
            return mapData;
        }

        #region - Tilemaps -

        public Tilemap GetTilemap(MapLayer layer) => tilemaps[(int)layer];

        public TileBase GetMapTileAt(MapLayer layer, Vector3 position) {
            return GetTilemap(layer).GetTile(Vector3Int.CeilToInt(position));
        }

        public TileType GetTileType(string name) {
            return (TileType)ContentLoader.GetContentByName(name);
        }

        public TileType GetTileType(TileBase tile) {
            return tile ? tileTypeDictionary[tile] : null;
        }

        public TileType GetMapTileTypeAt(MapLayer layer, Vector2 position) {
            return GetTileType(GetMapTileAt(layer, position));
        }

        public bool CanPlaceBlockAt(Vector2Int position, int size) {
            if (size == 1) return CanPlaceBlockAt(position);

            for (int x = 0; x < size; x++) {
                for (int y = 0; y < size; y++) {
                    Vector2Int sizePosition = position + new Vector2Int(x, y);
                    if (!CanPlaceBlockAt(sizePosition)) return false;
                }
            }

            return true;
        }

        public bool CanPlaceBlockAt(Vector2Int position) {
            TileType groundTile = GetMapTileTypeAt(MapLayer.Ground, position);
            bool solidTileExists = GetTilemap(MapLayer.Solid).HasTile((Vector3Int)position);
            return !solidTileExists && groundTile != null && groundTile.allowBuildings;
        }

        public void PlaceTile(MapLayer layer, Vector2Int position, TileBase tile, int size) {
            if (size == 1) {
                PlaceTile(layer, position, tile);
                return;
            }

            for (int x = 0; x < size; x++) {
                for (int y = 0; y < size; y++) {
                    Vector2Int sizePosition = position + new Vector2Int(x, y);
                    PlaceTile(layer, sizePosition, tile);
                }
            }
        }

        public void PlaceTile(MapLayer layer, Vector2Int position, TileBase tile) {
            GetTilemap(layer).SetTile((Vector3Int) position, tile);
        }

        private bool ContainsAnyTile(MapLayer layer, Vector2Int position, TileBase[] tiles) {
            foreach (TileBase tile in tiles) if (GetTilemap(layer).GetTile((Vector3Int)position) == tile) return true;
            return false;
        }

        public string[,,] AllMapsToStringArray() {
            int layers = (int)MapLayer.Total;
            string[,,] returnArray = new string[size.x, size.y, layers];

            for (int i = 0; i < layers; i++) {
                MapLayer layer = (MapLayer)i;
                Vector2Int offset = (Vector2Int)GetTilemap(layer).origin;

                for (int x = 0; x < size.x; x++) {
                    for (int y = 0; y < size.y; y++) {
                        Vector2Int position = new(x + offset.x, y + offset.y);
                        TileType tileType = GetMapTileTypeAt(layer, position);
                        returnArray[x, y, i] = tileType?.name;
                    }
                }
            }

            return returnArray;
        }




        /* Old map to string array for a single tilemap
        
        public string[,] MapToStringArray(MapLayer layer) {
            Tilemap tilemap = GetTilemap(layer);
            int width = tilemap.size.x;
            int height = tilemap.size.y;

            Vector2Int offset = (Vector2Int)tilemap.origin;

            string[,] returnArray = new string[width, height];

            for (int x = 0; x < width; x++) {
                for (int y = 0; y < height; y++) {
                    Vector3Int position = new(x + offset.x, y + offset.y, 0);
                    TileType tileType = GetMapTileTypeAt(layer, position);
                    returnArray[x, y] = tileType?.name;
                }
            }

            return returnArray;
        }
        */

        public string[] TilemapsToStringArray() {
            int layers = (int)MapLayer.Total;
            int i = 0;

            string[] tileData = new string[Mathf.CeilToInt(size.x * size.y * layers / MapLoader.TilesPerString) + 1];

            for (int z = 0; z < layers; z++) {
                MapLayer layer = (MapLayer)z;
                for (int x = 0; x < size.x; x++) {
                    for (int y = 0; y < size.y; y++) {
                        int stringIndex = Mathf.FloorToInt(i / MapLoader.TilesPerString);

                        Vector2Int position = new(x, y);
                        TileType tileType = GetMapTileTypeAt(layer, position);
                        tileData[stringIndex] += tileType == null ? (char)32 : (char)(tileType.id + 32);

                        i++;
                    }
                }
            }

            return tileData;
        }

        public void SetTilemapsFromString(Vector2Int size, string[] tileData) {
            int layers = (int)MapLayer.Total;
            int i = 0;

            for (int z = 0; z < layers; z++) {
                for (int x = 0; x < size.x; x++) {
                    for (int y = 0; y < size.y; y++) {
                        int stringIndex = Mathf.FloorToInt(i / MapLoader.TilesPerString);
                        int tile = i - (stringIndex * MapLoader.TilesPerString);

                        int id = Convert.ToInt32(tileData[stringIndex][tile]) - 32;
                        if (id == 0) continue;
                        TileType tileType = id == 0 ? null : (TileType)ContentLoader.GetContentById((short)id);

                        PlaceTile((MapLayer)z, new Vector2Int(x, y), tileType.GetRandomTileVariant());

                        i++;
                    }
                }
            }
        }

        public string[] BlocksToStringArray() {
            string[] blockArray = new string[Mathf.Max(blocks.Count, 2)];
            for (int i = 0; i < blockArray.Length; i++) blockArray[i] = blocks.Count <= i ? "null" : blocks[i].ToString();
            return blockArray;
        }

        public void SetBlocksFromStringArray(string[] blockData) {
            for(int i = 0; i < blockData.Length; i++) {
                string data = blockData[i];
                if (data == "null") continue;

                string[] values = data.Split(':');

                int syncID = int.Parse(values[0]);
                short contentID = short.Parse(values[1]);
                byte teamCode = byte.Parse(values[2]);
                float health = float.Parse(values[3]);
                Vector2 position = new(float.Parse(values[4]), float.Parse(values[5]));
                int orientation = int.Parse(values[6]);

                Block block = MapManager.Instance.InstantiateBlock(position, orientation, contentID, syncID, teamCode);
                block.SetHealth(health);
            }
        }

        #endregion

        public Entity GetClosestEntity(Vector2 position, byte teamCode) {
            Entity closestEntity = null;
            float closestDistance = 99999f;

            foreach (Entity entity in loadedEntities) {
                //If content doesn't match the filter, skip
                if (!(entity.GetTeam() == teamCode)) continue;

                //Get distance to content
                float distance = Vector2.Distance(position, entity.GetPosition());

                //If distance is lower than previous closest distance, set this as the closest content
                if (distance < closestDistance) {
                    closestDistance = distance;
                    closestEntity = entity;
                }
            }

            return closestEntity;
        }

        public Entity GetClosestEntity(Vector2 position, Type type, byte teamCode) {
            Entity closestEntity = null;
            float closestDistance = 99999f;

            foreach (Entity entity in loadedEntities) {
                //If content doesn't match the filter, skip
                if (entity.GetTeam() != teamCode || !TypeEquals(entity.GetType(), type)) continue;

                //Get distance to content
                float distance = Vector2.Distance(position, entity.GetPosition());

                //If distance is lower than previous closest distance, set this as the closest content
                if (distance < closestDistance) {
                    closestDistance = distance;
                    closestEntity = entity;
                }
            }

            return closestEntity;
        }

        public Entity GetClosestEntityStrict(Vector2 position, Type type, byte teamCode) {
            Entity closestEntity = null;
            float closestDistance = 99999f;

            foreach (Entity entity in loadedEntities) {
                //If content doesn't match the filter, skip
                if (entity.GetTeam() != teamCode || entity.GetType() != type) continue;

                //Get distance to content
                float distance = Vector2.Distance(position, entity.GetPosition());

                //If distance is lower than previous closest distance, set this as the closest content
                if (distance < closestDistance) {
                    closestDistance = distance;
                    closestEntity = entity;
                }
            }

            return closestEntity;
        }

        public Entity GetClosestEntityInView(Vector2 position, Vector2 direction, float fov, Type type, byte teamCode) {
            Entity closestEntity = null;
            float closestDistance = 99999f;

            foreach (Entity entity in loadedEntities) {
                //If content doesn't match the filter, skip
                if (entity.GetTeam() != teamCode || !TypeEquals(entity.GetType(), type)) continue;

                //If is not in view range continue to next
                float cosAngle = Vector2.Dot((entity.GetPosition() - position).normalized, direction);
                float angle = Mathf.Acos(cosAngle) * Mathf.Rad2Deg;


                //Get distance to content
                float distance = Vector2.Distance(position, entity.GetPosition());
                if (angle > fov) continue;

                //If distance is lower than previous closest distance, set this as the closest content
                if (distance < closestDistance) {
                    closestDistance = distance;
                    closestEntity = entity;
                }
            }

            return closestEntity;
        }

        #region - Blocks -

        public static bool TypeEquals(Type target, Type reference) => target == reference || target.IsSubclassOf(reference);

        public Block GetClosestBlock(Vector2 position, Type type, byte teamCode) {
            Block closestBlock = null;
            float closestDistance = 99999f;

            foreach (Block block in blocks) {
                //If block doesn't match the filter, skip
                if (block.GetTeam() != teamCode || !TypeEquals(block.GetType(), type)) continue;

                //Get distance to block
                float distance = Vector2.Distance(position, block.GetPosition());

                //If distance is lower than previous closest distance, set this as the closest block
                if (distance < closestDistance) {
                    closestDistance = distance;
                    closestBlock = block;
                }
            }

            return closestBlock;
        }

        public LandPadBlock GetBestAvilableLandPad(Unit forUnit) {
            LandPadBlock smallestLandPad = null;
            float smallestSize = 99999f;

            foreach (Block block in blocks) {
                if (block is LandPadBlock landPad) {

                    //If block doesn't match the filter, skip
                    if (!landPad.CanLand(forUnit)) continue;

                    //Get size to block
                    float size = landPad.Type.unitSize;

                    //If size is lower than previous closest distance, set this as the closest block
                    if (size < smallestSize) {
                        smallestSize = size;
                        smallestLandPad = landPad;
                    }
                }
            }

            return smallestLandPad;
        }

        public Block GetBlockAt(Vector2Int position) {
            foreach (Block block in blocks) {
                // Check if block is close enough to maybe be in the position
                float distance = Vector2.Distance(block.GetPosition(), position);
                if (distance > block.Type.size) continue;

                // Do a full on check
                if (block.GetGridPosition() == position) return block;
                if (block.ExistsIn(Vector2Int.CeilToInt(position))) return block;
            }

            return null;
        }

        public List<ItemBlock> GetAdjacentBlocks(ItemBlock itemBlock) {
            List<ItemBlock> adjacentBlocks = new List<ItemBlock>();
            foreach (Block other in blocks) {
                if (TypeEquals(other.GetType(), typeof(ItemBlock)) && itemBlock != other && itemBlock.IsNear(other)) adjacentBlocks.Add(other as ItemBlock);
            }
            return adjacentBlocks;
        }

        public void AddBlock(Block block) {
            blocks.Add(block);
            loadedEntities.Add(block);
        }

        public void RemoveBlock(Block block) {
            blocks.Remove(block);
            loadedEntities.Remove(block);
        }

        #endregion

        #region - Units -

        public void AddUnit(Unit unit) {
            units.Add(unit);
            loadedEntities.Add(unit);
        }

        public void RemoveUnit(Unit unit) {
            units.Remove(unit);
            loadedEntities.Remove(unit);
        }

        #endregion
    }

    [Serializable]
    public struct MapData {
        public Vector2Int size;
        public TilemapData tilemapData;

        public MapData(Vector2Int size, TilemapData tileData) {
            this.size = size;
            this.tilemapData = tileData;
        }

        public MapData(Map map) {
            size = map.size;
            tilemapData = new TilemapData(map.AllMapsToStringArray());
        }

        public MapData(Vector2Int size, string[] tileData) {
            this.size = size;
            this.tilemapData = new TilemapData(size, tileData);
        }

        /*
        public BlockArrayData blockData;
        public UnitArrayData unitData;

        public MapData(TileMapData tileData, BlockArrayData blockData, UnitArrayData unitData) {
            this.tileData = tileData;
            this.blockData = blockData;
            this.unitData = unitData;
        }*/

        [Serializable]
        public struct TilemapData {
            public Wrapper2D<string> tileReferenceGrid;

            public TilemapData(string[,] tileReferenceGrid) {
                this.tileReferenceGrid = new Wrapper2D<string>(tileReferenceGrid);
            }

            public TilemapData(string[,,] tileNameGrid) {
                tileReferenceGrid = new Wrapper2D<string>(Encode(tileNameGrid));
            }

            public TilemapData(Vector2Int size, string[] tileData) {
                tileReferenceGrid = new Wrapper2D<string>(ReAssemble(size, tileData));
            }

            public string[,,] DecodeThis() => Decode(tileReferenceGrid.array2D);

            public static string[,] Encode(string[,,] tileNameGrid) {
                Vector3Int size = new(tileNameGrid.GetLength(0), tileNameGrid.GetLength(1), tileNameGrid.GetLength(2));
                string[,] returnGrid = new string[size.x, size.y];

                for(int x = 0; x < size.x; x++) {
                    for (int y = 0; y < size.y; y++) {
                        for (int z = 0; z < size.z; z++) {
                            string name = tileNameGrid[x, y, z];
                            returnGrid[x, y] += name == null ? (char)32 : (char)(ContentLoader.GetContentByName(name).id + 32);
                        }
                    }
                }

                return returnGrid;
            }

            public static string[,] ReAssemble(Vector2Int size, string[] tileData) {
                int layers = (int)Map.MapLayer.Total;
                string[,] returnGrid = new string[size.x, size.y];
                int i = 0;

                for (int z = 0; z < layers; z++) {
                    for (int x = 0; x < size.x; x++) {
                        for (int y = 0; y < size.y; y++) {

                            int stringIndex = Mathf.FloorToInt(i / MapLoader.TilesPerString);
                            int tile = i - (stringIndex * MapLoader.TilesPerString);

                            returnGrid[x, y] += tileData[stringIndex][tile];
                            i++;
                        }
                    }
                }

                return returnGrid;
            }

            public static string[,,] Decode(string[,] tileReferenceGrid) {
                Vector2Int size = new(tileReferenceGrid.GetLength(0), tileReferenceGrid.GetLength(1));

                int layers = (int)Map.MapLayer.Total;
                string[,,] returnGrid = new string[size.x, size.y, layers];

                for (int x = 0; x < size.x; x++) {
                    for (int y = 0; y < size.y; y++) {
                        string tileData = tileReferenceGrid[x, y];

                        for (int z = 0; z < layers; z++) {
                            int value = Convert.ToInt32(tileData[z]) - 32;
                            if (value == 0) continue;
                            returnGrid[x, y, z] = value == 0 ? null : ContentLoader.GetContentById((short)value).name;
                        }
                    }
                }

                return returnGrid;
            }
        }
        /*
        [Serializable]
        public struct BlockArrayData {
            public BlockData[] array;

            public BlockArrayData(BlockData[] array) {
                this.array = array;
            }

            [Serializable]
            public struct BlockData {
                public Vector2Int position;
            }
        }

        [Serializable]
        public struct UnitArrayData {
            public UnitData[] array;

            public UnitArrayData(UnitData[] array) {
                this.array = array;
            }

            [Serializable]
            public struct UnitData {
                public Vector2 position;
            }
        }
        */
    }

    public class BuildPlan {
        public event EventHandler<EventArgs> OnPlanFinished;
        public event EventHandler<EventArgs> OnPlanCanceled;

        public BlockType blockType;
        public Inventory missingItems;
        public Vector2Int position;
        public int orientation;
        public bool breaking;

        public float progress;
        public bool hasStarted, isStuck;

        public BuildPlan(BlockType blockType, Vector2Int position, int orientation) {
            this.blockType = blockType;
            this.position = position;
            this.orientation = orientation;

            missingItems = new Inventory();
            missingItems.Add(blockType.buildCost);
        }

        public void AddItems(ItemStack[] stacks) {
            missingItems.Substract(stacks);
            float progress = BuildProgress();
            if (progress >= 1f) OnPlanFinished?.Invoke(this, EventArgs.Empty);
        }

        public float BuildProgress() {
            float total = 0;

            for(int i = 0; i < missingItems.items.Count; i++) {
                Item item = blockType.buildCost[i].item;
                int neededAmount = blockType.buildCost[i].amount;

                total += (neededAmount - missingItems.items[item]) / neededAmount;
            }

            total /= missingItems.items.Count;

            return total;
        }

        public void Cancel() {
            OnPlanCanceled?.Invoke(this, EventArgs.Empty);
        }
    }
}

public interface IDamageable {
    public void Damage(float amount);
}

public interface IView {
    public PhotonView PhotonView { get; set; }
}

public interface IInventory {
    public Inventory GetInventory();
}

public interface IArmed {
    public Weapon GetWeaponByID(int ID);
}