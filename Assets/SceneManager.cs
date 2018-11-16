using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace Frame.SceneManagement
{
    public interface ISceneManager
    {
        string CurrentScene { get; }
        int CurrentSceneIndex { get; }
        void ChangeScene(int index);
        void ChangeScene(string name);
    }

    public class SceneManager : ISceneManager, IInitializable 
    {
        public string CurrentScene { get; private set; }
        public int CurrentSceneIndex { get; private set; }
        public void ChangeScene(int index)
        {
            throw new System.NotImplementedException();
        }

        public void ChangeScene(string name)
        {
            throw new System.NotImplementedException();
        }

        public void Initialize()
        {
            Debug.Log("SceneManager Initialized");
        }
    }
}
