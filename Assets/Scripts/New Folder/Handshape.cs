using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NewHandShape", menuName = "BSL/Hand Shape")]
public class HandShape : ScriptableObject
{
    [Serializable]
    public struct FingerData
    {
        public int fingerId; // 0-4
        public float curl;   // 0 (extended) to 1 (fully curled)
    }

    public string handName;
    public FingerData[] fingerCurls;
}

