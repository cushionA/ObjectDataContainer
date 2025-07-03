using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MyTool
{
    /// <summary>
    /// �Œ�T�C�Y�E�X���b�v�폜�ł̃L�����N�^�[�f�[�^����
    /// �ő�e�ʂ����O�Ɋm�ۂ����T�C�Y���Ȃ�
    /// �폜���͍폜�����ƍ��̍Ō�̗v�f�����ւ��邱�ƂŃf�[�^���f�Љ����Ȃ�
    /// �n�b�V���e�[�u���ɂ��GetComponent�s�v�Ńf�[�^�A�N�Z�X���\
    /// </summary>
    //[ContainerSetting(
    //    structType: new[] {
    //        typeof(CharacterBaseInfo),
    //        typeof(CharacterAtkStatus),
    //        typeof(CharacterDefStatus),
    //        typeof(SolidData),
    //        typeof(CharacterStateInfo),
    //        typeof(MoveStatus),
    //        typeof(CharacterColdLog)
    //    },
    //    classType: new[] { typeof(BaseController) }
    //)]
    public unsafe partial class ObjectDataContainer : IDisposable
    {
        #region �萔

        /// <summary>
        /// �f�t�H���g�̍ő�e��
        /// </summary>
        private const int DEFAULT_MAX_CAPACITY = 130;

        /// <summary>
        /// �o�P�b�g���i�n�b�V���e�[�u���̃T�C�Y�j
        /// </summary>
        private const int BUCKET_COUNT = 191;  // �f�����g�p

        #endregion

        #region �R���X�g���N�^

        /// <summary>
        /// �R���X�g���N�^
        /// </summary>
        /// <param name="maxCapacity">�ő�e�ʁi�f�t�H���g: 100�j</param>
        /// <param name="allocator">�������A���P�[�^�i�f�t�H���g: Persistent�j</param>
        public ObjectDataContainer(int maxCapacity = DEFAULT_MAX_CAPACITY, Allocator allocator = Allocator.Persistent)
        {
            InitializeContainer(maxCapacity, allocator);
        }

        /// <summary>
        /// �R���e�i�̏����������iSource Generator�Ŏ��������j
        /// </summary>
        partial void InitializeContainer(int maxCapacity, Allocator allocator);

        #endregion

        #region ���[�e�B���e�B

        /// <summary>
        /// ���ׂẴG���g�����N���A
        /// </summary>
        public void Clear()
        {
            ClearAllData();
        }

        /// <summary>
        /// �f�[�^�N���A�����iSource Generator�Ŏ��������j
        /// </summary>
        partial void ClearAllData();

        #endregion

        // ContainsKey �� ContainsKeyByHash �͐����R�[�h�Ŋ��S���������

        #region IDisposable

        /// <summary>
        /// ���\�[�X�̉��
        /// </summary>
        public void Dispose()
        {
            DisposeResources();
        }

        /// <summary>
        /// ���\�[�X��������iSource Generator�Ŏ��������j
        /// </summary>
        partial void DisposeResources();

        #endregion
    }
}