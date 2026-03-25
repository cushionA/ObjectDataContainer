using System;
using System.Collections.Generic;

namespace ODC.Runtime
{
    /// <summary>
    /// 複数参加者が順番にステップをクリアするシーケンスを管理するコンテナ。
    /// 各ステップには時間ウィンドウがあり、超過するとシーケンスが失敗/リセットされる。
    /// TState は各ステップの共有状態を格納する unmanaged 構造体。
    /// </summary>
    /// <typeparam name="TState">シーケンスの共有状態型（unmanaged構造体）</typeparam>
    public class MultiPartySequenceContainer<TState> : IDisposable where TState : unmanaged
    {
        /// <summary>
        /// シーケンスデータ構造体。
        /// </summary>
        private struct SequenceData
        {
            public int SequenceId;
            public TState CurrentState;
            public int ParticipantStartIndex;  // _participants配列でのオフセット
            public int ParticipantCount;
            public int CurrentStep;
            public int TotalSteps;
            public float WindowPerStep;
            public float StepElapsed;
            public bool IsCompleted;
        }

        /// <summary>
        /// 参加者エントリ。
        /// </summary>
        private struct Participant
        {
            public int Hash;
            public bool HasAdvancedThisStep;
        }

        /// <summary>
        /// ハッシュテーブルのエントリ構造体。
        /// </summary>
        private struct Entry
        {
            public int HashCode;
            public int ValueIndex;
            public int NextInBucket;
        }

        /// <summary>バケットサイズ決定用の素数テーブル</summary>
        private static readonly int[] Primes = { 7, 17, 37, 79, 163, 331, 673, 1361, 2729, 5471, 10949, 15497 };

        private SequenceData[] _sequences;
        private int _sequenceCount;
        private int _maxSequences;

        private Participant[] _participants;
        private int _participantCount;
        private int _maxParticipants;

        private int _nextSequenceId;

        // シーケンスID → シーケンスインデックスのハッシュテーブル
        private int[] _buckets;
        private Entry[] _entries;
        private int _entryCount;
        private int _bucketCount;
        private Stack<int> _freeEntries;

        /// <summary>現在のアクティブシーケンス数</summary>
        public int ActiveCount => _sequenceCount;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="maxSequences">最大シーケンス数</param>
        /// <param name="maxParticipantsTotal">最大参加者総数（デフォルト: maxSequences * 8）</param>
        public MultiPartySequenceContainer(int maxSequences, int maxParticipantsTotal = -1)
        {
            _maxSequences = maxSequences;
            _maxParticipants = maxParticipantsTotal > 0 ? maxParticipantsTotal : maxSequences * 8;
            _bucketCount = GetPrimeBucketCount(maxSequences);

            _sequences = new SequenceData[maxSequences];
            _sequenceCount = 0;

            _participants = new Participant[_maxParticipants];
            _participantCount = 0;

            _nextSequenceId = 1;

            _buckets = new int[_bucketCount];
            _entries = new Entry[maxSequences];
            _entryCount = 0;
            _freeEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// 新しいシーケンスを開始する。
        /// </summary>
        /// <param name="participantHashes">参加者のハッシュ配列</param>
        /// <param name="totalSteps">総ステップ数</param>
        /// <param name="windowPerStep">各ステップの入力猶予時間（秒）</param>
        /// <param name="initialState">初期状態</param>
        /// <returns>シーケンスID</returns>
        public int Begin(int[] participantHashes, int totalSteps, float windowPerStep, in TState initialState)
        {
            if (_sequenceCount >= _maxSequences)
                throw new InvalidOperationException("シーケンス数が上限に達しています。");
            if (participantHashes == null || participantHashes.Length == 0)
                throw new ArgumentException("参加者が必要です。");
            if (_participantCount + participantHashes.Length > _maxParticipants)
                throw new InvalidOperationException("参加者総数が上限に達しています。");

            int seqId = _nextSequenceId++;
            int seqIndex = _sequenceCount;
            int partStart = _participantCount;

            // 参加者を登録
            for (int i = 0; i < participantHashes.Length; i++)
            {
                _participants[_participantCount] = new Participant
                {
                    Hash = participantHashes[i],
                    HasAdvancedThisStep = false
                };
                _participantCount++;
            }

            _sequences[seqIndex] = new SequenceData
            {
                SequenceId = seqId,
                CurrentState = initialState,
                ParticipantStartIndex = partStart,
                ParticipantCount = participantHashes.Length,
                CurrentStep = 0,
                TotalSteps = totalSteps,
                WindowPerStep = windowPerStep,
                StepElapsed = 0f,
                IsCompleted = false
            };
            _sequenceCount++;

            RegisterToHashTable(seqId, seqIndex);

            return seqId;
        }

        /// <summary>
        /// 参加者がステップをクリアしたことを報告する。
        /// 全参加者がadvanceした場合、次のステップに進む。
        /// </summary>
        /// <param name="sequenceId">シーケンスID</param>
        /// <param name="participantHash">報告する参加者のハッシュ</param>
        /// <param name="newState">更新後の状態</param>
        /// <returns>次ステップに進んだ場合true</returns>
        public bool Advance(int sequenceId, int participantHash, in TState newState)
        {
            if (!TryGetIndexByHash(sequenceId, out int seqIndex))
                return false;

            ref SequenceData seq = ref _sequences[seqIndex];
            if (seq.IsCompleted) return false;

            // 参加者を検索してフラグを立てる
            int start = seq.ParticipantStartIndex;
            int end = start + seq.ParticipantCount;
            bool found = false;
            for (int i = start; i < end; i++)
            {
                if (_participants[i].Hash == participantHash && !_participants[i].HasAdvancedThisStep)
                {
                    _participants[i].HasAdvancedThisStep = true;
                    found = true;
                    break;
                }
            }

            if (!found) return false;

            // 全参加者がadvanceしたか確認
            bool allAdvanced = true;
            for (int i = start; i < end; i++)
            {
                if (!_participants[i].HasAdvancedThisStep)
                {
                    allAdvanced = false;
                    break;
                }
            }

            if (allAdvanced)
            {
                seq.CurrentState = newState;
                seq.CurrentStep++;
                seq.StepElapsed = 0f;

                if (seq.CurrentStep >= seq.TotalSteps)
                {
                    seq.IsCompleted = true;
                }
                else
                {
                    // 参加者フラグをリセット
                    for (int i = start; i < end; i++)
                        _participants[i].HasAdvancedThisStep = false;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// シーケンスの状態を取得する。
        /// </summary>
        public bool TryGetSequence(int sequenceId, out TState state, out int currentStep, out bool isCompleted)
        {
            if (!TryGetIndexByHash(sequenceId, out int seqIndex))
            {
                state = default;
                currentStep = 0;
                isCompleted = false;
                return false;
            }

            state = _sequences[seqIndex].CurrentState;
            currentStep = _sequences[seqIndex].CurrentStep;
            isCompleted = _sequences[seqIndex].IsCompleted;
            return true;
        }

        /// <summary>
        /// シーケンスが完了しているか。
        /// </summary>
        public bool IsCompleted(int sequenceId)
        {
            if (!TryGetIndexByHash(sequenceId, out int seqIndex))
                return false;
            return _sequences[seqIndex].IsCompleted;
        }

        /// <summary>
        /// シーケンスを明示的に終了する。
        /// </summary>
        public void End(int sequenceId)
        {
            if (!TryGetIndexByHash(sequenceId, out int seqIndex))
                return;
            RemoveSequenceAtIndex(seqIndex);
        }

        /// <summary>
        /// 時間ウィンドウ更新。タイムアウトしたシーケンスを自動終了。
        /// </summary>
        public void Tick(float deltaTime)
        {
            for (int i = _sequenceCount - 1; i >= 0; i--)
            {
                if (_sequences[i].IsCompleted) continue;

                _sequences[i].StepElapsed += deltaTime;
                if (_sequences[i].WindowPerStep > 0f &&
                    _sequences[i].StepElapsed >= _sequences[i].WindowPerStep)
                {
                    // タイムアウト → シーケンス終了
                    RemoveSequenceAtIndex(i);
                }
            }
        }

        /// <summary>
        /// コンテナの全データをクリアする。
        /// </summary>
        public void Clear()
        {
            _sequenceCount = 0;
            _participantCount = 0;
            _entryCount = 0;
            _freeEntries.Clear();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _sequences = null;
            _participants = null;
            _buckets = null;
            _entries = null;
            _freeEntries = null;
        }

        // =============================================
        // 内部ヘルパー
        // =============================================

        private void RemoveSequenceAtIndex(int seqIndex)
        {
            int seqId = _sequences[seqIndex].SequenceId;
            int removedPartStart = _sequences[seqIndex].ParticipantStartIndex;
            int removedPartCount = _sequences[seqIndex].ParticipantCount;

            // 参加者配列をcompact: 削除範囲以降を前に詰める
            int removedPartEnd = removedPartStart + removedPartCount;
            if (removedPartEnd < _participantCount)
            {
                Array.Copy(_participants, removedPartEnd, _participants, removedPartStart,
                    _participantCount - removedPartEnd);
            }
            _participantCount -= removedPartCount;

            // 全シーケンスのParticipantStartIndexを更新
            for (int i = 0; i < _sequenceCount; i++)
            {
                if (i == seqIndex) continue;
                if (_sequences[i].ParticipantStartIndex > removedPartStart)
                {
                    _sequences[i].ParticipantStartIndex -= removedPartCount;
                }
            }

            // シーケンス配列のBackSwap
            int lastSeqIndex = _sequenceCount - 1;
            if (seqIndex != lastSeqIndex)
            {
                int movedSeqId = _sequences[lastSeqIndex].SequenceId;
                _sequences[seqIndex] = _sequences[lastSeqIndex];
                UpdateEntryDataIndex(movedSeqId, seqIndex);
            }

            _sequences[lastSeqIndex] = default;
            RemoveFromHashTable(seqId);
            _sequenceCount--;
        }

        // =============================================
        // ハッシュテーブル操作
        // =============================================

        private int GetBucketIndex(int hashCode)
        {
            return (hashCode & 0x7FFFFFFF) % _bucketCount;
        }

        private void RegisterToHashTable(int hashCode, int valueIndex)
        {
            int bucketIndex = GetBucketIndex(hashCode);

            int entryIndex;
            if (_freeEntries.Count > 0)
            {
                entryIndex = _freeEntries.Pop();
            }
            else
            {
                entryIndex = _entryCount;
                _entryCount++;
            }

            _entries[entryIndex] = new Entry
            {
                HashCode = hashCode,
                ValueIndex = valueIndex,
                NextInBucket = _buckets[bucketIndex]
            };
            _buckets[bucketIndex] = entryIndex;
        }

        private void RemoveFromHashTable(int hashCode)
        {
            int bucketIndex = GetBucketIndex(hashCode);
            int prev = -1;
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_entries[current].HashCode == hashCode)
                {
                    if (prev == -1)
                        _buckets[bucketIndex] = _entries[current].NextInBucket;
                    else
                        _entries[prev].NextInBucket = _entries[current].NextInBucket;

                    _freeEntries.Push(current);
                    return;
                }
                prev = current;
                current = _entries[current].NextInBucket;
            }
        }

        private bool TryGetIndexByHash(int hashCode, out int valueIndex)
        {
            int bucketIndex = GetBucketIndex(hashCode);
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_entries[current].HashCode == hashCode)
                {
                    valueIndex = _entries[current].ValueIndex;
                    return true;
                }
                current = _entries[current].NextInBucket;
            }

            valueIndex = -1;
            return false;
        }

        private void UpdateEntryDataIndex(int hashCode, int newDataIndex)
        {
            int bucketIndex = GetBucketIndex(hashCode);
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_entries[current].HashCode == hashCode)
                {
                    _entries[current].ValueIndex = newDataIndex;
                    return;
                }
                current = _entries[current].NextInBucket;
            }
        }

        private static int GetPrimeBucketCount(int capacity)
        {
            int target = (int)(capacity * 1.5f);
            for (int i = 0; i < Primes.Length; i++)
            {
                if (Primes[i] >= target)
                    return Primes[i];
            }
            return Primes[Primes.Length - 1];
        }
    }
}
