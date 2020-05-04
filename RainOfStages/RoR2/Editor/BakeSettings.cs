﻿using System;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using RainOfStages.Utilities;
using UnityEditor;
#endif

namespace RainOfStages
{
    [CreateAssetMenu(menuName = "Rain of Stages/Bake Settings")]
    public class BakeSettings : ScriptableObject
    {
        public Boolean DebugMode;
        public bool showMesh;


        public Material DebugMaterial;
        public int agentTypeID;
        public float agentRadius;
        public float agentHeight;
        public float agentSlope;
        public float agentClimb;
        public float minRegionArea;
        public bool overrideVoxelSize;
        public float voxelSize;
        public bool overrideTileSize;
        public int tileSize;
        public Vector3 globalNavigationOffset;

        [HideInInspector]
        public GameObject NodeMeshObject;

        public NavMeshBuildSettings bakeSettings => new NavMeshBuildSettings
        {
            agentClimb = agentClimb,
            agentHeight = agentHeight,
            agentRadius = agentRadius,
            agentSlope = agentSlope,
            agentTypeID = agentTypeID,
            minRegionArea = minRegionArea,
            overrideTileSize = overrideTileSize,
            overrideVoxelSize = overrideVoxelSize,
            tileSize = tileSize
        };

        private void OnValidate()
        {
            if (NodeMeshObject)
                NodeMeshObject.SetActive(showMesh);
        }

#if UNITY_EDITOR
        [MenuItem("Assets/Rain of Stages/Stages/" + nameof(BakeSettings))]
        public static void Create() => ScriptableHelper.CreateAsset<BakeSettings>();
#endif
    }
}