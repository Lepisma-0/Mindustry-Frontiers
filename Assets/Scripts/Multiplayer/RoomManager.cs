using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Realtime;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using UnityEngine.SceneManagement;
using System.IO;
using System.Linq;
using Photon.Pun.UtilityScripts;
using System;
using ExitGames.Client.Photon;
using Frontiers.Teams;

public class RoomManager : MonoBehaviourPunCallbacks {
    public static RoomManager Instance;
    public PhotonTeamsManager photonTeamsManager;

    private float switchButtonCooldown;
    private bool updateManagers = false;

    #region - Unity callbacks -

    private void Awake() {
        if (Instance) {
            Destroy(gameObject);
            return;
        }

        if (!photonTeamsManager) photonTeamsManager = GetComponent<PhotonTeamsManager>();
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;

        Instance = this;
    }

    private void Update() {
        if (!updateManagers) return;

        // Update managers
        MapManager.Instance.UpdateMapManager();
        //PlayerManager.Instance.UpdatePlayerManager();
    }

    #endregion

    #region - Room -

    public override void OnJoinedRoom() {
        if (!TeamUtilities.IsMaster()) return;
        PhotonNetwork.LocalPlayer.JoinTeam(TeamUtilities.GetDefaultTeam());
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) {
        if (!TeamUtilities.IsMaster()) return;
        newPlayer.JoinTeam(TeamUtilities.GetDefaultTeam());
    }

    public override void OnPlayerLeftRoom(Player otherPlayer) {
        if (!TeamUtilities.IsMaster()) return;
        otherPlayer.LeaveCurrentTeam();
    }

    public void SwitchTeam() {
        if (switchButtonCooldown >= Time.time) return;
        switchButtonCooldown = Time.time + 1f;
        PhotonNetwork.LocalPlayer.SwitchTeam(TeamUtilities.GetEnemyTeam(TeamUtilities.GetLocalPlayerTeam().Code));
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode) {
        if (scene.buildIndex == 1) {
            InitializeMatch();
        }
    }

    void InitializeMatch() {
        // Initialize managers
        PlayerManager.InitializePlayerManager();
        MapManager.InitializeMapManager();

        // If this client is the master, spawn cores
        if (TeamUtilities.IsMaster()) MapManager.Instance.InitializeCores();
        updateManagers = true;
    }

    #endregion
}