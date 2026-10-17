
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Serialization;

namespace nBriefcase
{
    public enum SyncStatus
    {
        Registered,
        NoChange,
        SourceCopiedToBriefcase,
        BriefcaseCopiedToSource,
        Conflict,
        SourceMissing,
        BriefcaseMissing,
        BothMissing
    }
    public class SyncItemResult
    {
        public SyncStatus Status { get; set; }
        public string Message { get; set; }
    }
    [Serializable]
    public class SyncManifest
    {
        public int FormatVersion = 1;

        public List<SyncRecord> Records = new List<SyncRecord>();
    }

    [Serializable]
    public class SyncRecord
    {
        // 컴퓨터에 있는 원본 파일의 전체 경로
        public string SourcePath = "";

        // VHDX 내부에서의 상대 경로
        public string RelativePath = "";

        // 마지막으로 동기화가 완료된 파일의 SHA-256 해시
        public string BaselineHash = "";
    }

    public static class SyncManager
    {
        private const string ManifestFileName = ".nbrief-sync.xml";

        public static string GetManifestPath(string briefcaseRoot)
        {
            return Path.Combine(
                briefcaseRoot,
                ManifestFileName);
        }

        public static SyncManifest LoadManifest(string briefcaseRoot)
        {
            string path = GetManifestPath(briefcaseRoot);

            if (!File.Exists(path))
                return new SyncManifest();

            XmlSerializer serializer =
                new XmlSerializer(typeof(SyncManifest));

            using (FileStream stream = File.OpenRead(path))
            {
                SyncManifest manifest =
                    (SyncManifest)serializer.Deserialize(stream);

                if (manifest == null || manifest.FormatVersion != 1)
                {
                    throw new InvalidDataException(
                        "지원하지 않는 동기화 기록 형식입니다.");
                }

                if (manifest.Records == null)
                    manifest.Records = new List<SyncRecord>();

                return manifest;
            }
        }

        public static void SaveManifest(
            string briefcaseRoot,
            SyncManifest manifest)
        {
            string path = GetManifestPath(briefcaseRoot);
            string tempPath = path + ".tmp";

            XmlSerializer serializer =
                new XmlSerializer(typeof(SyncManifest));

            try
            {
                using (FileStream stream = File.Create(tempPath))
                {
                    serializer.Serialize(stream, manifest);
                }

                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public static string CalculateHash(string filePath)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                byte[] hash = sha256.ComputeHash(stream);

                return BitConverter.ToString(hash)
                    .Replace("-", "");
            }
        }

        public static SyncItemResult RegisterFile(
            string sourcePath,
            string briefcaseRoot,
            SyncManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "등록할 원본 파일을 찾을 수 없습니다.",
                    sourcePath);
            }

            if (string.IsNullOrWhiteSpace(briefcaseRoot) ||
                !Directory.Exists(briefcaseRoot))
            {
                throw new DirectoryNotFoundException(
                    "서류가방 드라이브를 찾을 수 없습니다.");
            }

            if (manifest == null)
                throw new ArgumentNullException("manifest");

            sourcePath = Path.GetFullPath(sourcePath);
            briefcaseRoot = Path.GetFullPath(briefcaseRoot);

            // 같은 원본을 중복 등록하지 않음
            foreach (SyncRecord existing in manifest.Records)
            {
                if (string.Equals(
                    Path.GetFullPath(existing.SourcePath),
                    sourcePath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return new SyncItemResult
                    {
                        Status = SyncStatus.NoChange,
                        Message = "이 원본 파일은 이미 등록되어 있습니다."
                    };
                }
            }

            // 원본 파일이 서류가방 내부에 있는 경우 등록 방지
            string rootPrefix = briefcaseRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (sourcePath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "서류가방 내부 파일을 원본으로 등록할 수 없습니다.");
            }

            // 드라이브별로 폴더를 구분해 원본 폴더 구조 유지
            string sourceRoot = Path.GetPathRoot(sourcePath);

            string driveFolder = sourceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                .Replace(":", "");

            if (string.IsNullOrWhiteSpace(driveFolder))
                driveFolder = "Network";

            string sourceRelativePath =
                sourcePath.Substring(sourceRoot.Length);

            string relativePath = Path.Combine(
                driveFolder,
                sourceRelativePath);

            string destinationPath = Path.Combine(
                briefcaseRoot,
                relativePath);

            string destinationDirectory =
                Path.GetDirectoryName(destinationPath);

            Directory.CreateDirectory(destinationDirectory);

            // 같은 이름이 있으면 (2), (3) 등을 붙임
            string originalDestination = destinationPath;
            int number = 2;

            while (File.Exists(destinationPath))
            {
                string directory =
                    Path.GetDirectoryName(originalDestination);

                string name =
                    Path.GetFileNameWithoutExtension(originalDestination);

                string extension =
                    Path.GetExtension(originalDestination);

                destinationPath = Path.Combine(
                    directory,
                    name + " (" + number + ")" + extension);

                number++;
            }

            // 실제 복사본 경로를 기준으로 상대 경로 계산
            relativePath = destinationPath.Substring(
                briefcaseRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar).Length + 1);

            CopyFileSafely(sourcePath, destinationPath);

            try
            {
                SyncRecord record = new SyncRecord
                {
                    SourcePath = sourcePath,
                    RelativePath = relativePath,
                    BaselineHash = CalculateHash(destinationPath)
                };

                manifest.Records.Add(record);
                SaveManifest(briefcaseRoot, manifest);
            }
            catch
            {
                // 기록 저장 실패 시 복사본을 고아 파일로 남기지 않음
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                throw;
            }

            return new SyncItemResult
            {
                Status = SyncStatus.Registered,
                Message = "파일 등록 완료: " +
                    Path.GetFileName(destinationPath)
            };
        }

        public static SyncItemResult SynchronizeRecord(
            string briefcaseRoot,
            SyncManifest manifest,
            SyncRecord record)
        {
            if (string.IsNullOrWhiteSpace(briefcaseRoot))
                throw new ArgumentException("서류가방 경로가 비어 있습니다.");

            if (manifest == null || record == null)
                throw new ArgumentNullException("동기화 기록이 없습니다.");

            if (string.IsNullOrWhiteSpace(record.SourcePath) ||
                string.IsNullOrWhiteSpace(record.RelativePath))
            {
                throw new InvalidDataException("파일 경로가 올바르지 않습니다.");
            }

            string root = Path.GetFullPath(briefcaseRoot);
            string relativePath = record.RelativePath;

            // 상대 경로를 이용해 서류가방 밖의 파일에 접근하는 것을 방지
            if (Path.IsPathRooted(relativePath))
                throw new InvalidDataException("서류가방 내부 경로가 아닙니다.");

            string copyPath = Path.GetFullPath(
                Path.Combine(root, relativePath));

            string rootPrefix = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!copyPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "서류가방 바깥의 경로는 사용할 수 없습니다.");
            }

            string sourcePath = Path.GetFullPath(record.SourcePath);

            bool sourceExists = File.Exists(sourcePath);
            bool copyExists = File.Exists(copyPath);

            if (!sourceExists && !copyExists)
            {
                return new SyncItemResult
                {
                    Status = SyncStatus.BothMissing,
                    Message = "원본과 서류가방 파일이 모두 없습니다."
                };
            }

            if (!sourceExists)
            {
                return new SyncItemResult
                {
                    Status = SyncStatus.SourceMissing,
                    Message = "컴퓨터 원본이 없습니다. 자동 삭제나 복구를 하지 않았습니다."
                };
            }

            if (!copyExists)
            {
                return new SyncItemResult
                {
                    Status = SyncStatus.BriefcaseMissing,
                    Message = "서류가방 파일이 없습니다. 자동 삭제나 복구를 하지 않았습니다."
                };
            }

            string sourceHash = CalculateHash(sourcePath);
            string copyHash = CalculateHash(copyPath);
            string baseline = record.BaselineHash ?? "";

            // 최초 등록 시 기준 해시가 없다면,
            // 두 파일의 내용이 같을 때만 기준을 설정
            if (string.IsNullOrEmpty(baseline))
            {
                if (sourceHash == copyHash)
                {
                    record.BaselineHash = sourceHash;
                    SaveManifest(briefcaseRoot, manifest);

                    return new SyncItemResult
                    {
                        Status = SyncStatus.NoChange,
                        Message = "최초 동기화 기준을 설정했습니다."
                    };
                }

                return new SyncItemResult
                {
                    Status = SyncStatus.Conflict,
                    Message = "최초 동기화 기준이 없고 두 파일의 내용도 다릅니다."
                };
            }

            bool sourceChanged = sourceHash != baseline;
            bool copyChanged = copyHash != baseline;

            // 둘 다 변경되지 않음
            if (!sourceChanged && !copyChanged)
            {
                return new SyncItemResult
                {
                    Status = SyncStatus.NoChange,
                    Message = "변경 사항이 없습니다."
                };
            }

            // 양쪽이 변경됐지만 결과 내용은 같음
            if (sourceChanged && copyChanged &&
                sourceHash == copyHash)
            {
                record.BaselineHash = sourceHash;
                SaveManifest(briefcaseRoot, manifest);

                return new SyncItemResult
                {
                    Status = SyncStatus.NoChange,
                    Message = "양쪽 파일의 내용이 같아 기준을 갱신했습니다."
                };
            }

            // 컴퓨터 원본만 변경됨
            if (sourceChanged && !copyChanged)
            {
                CopyFileSafely(sourcePath, copyPath);

                record.BaselineHash = CalculateHash(copyPath);
                SaveManifest(briefcaseRoot, manifest);

                return new SyncItemResult
                {
                    Status = SyncStatus.SourceCopiedToBriefcase,
                    Message = "컴퓨터 원본을 서류가방으로 복사했습니다."
                };
            }

            // 서류가방 복사본만 변경됨
            if (!sourceChanged && copyChanged)
            {
                CopyFileSafely(copyPath, sourcePath);

                record.BaselineHash = CalculateHash(sourcePath);
                SaveManifest(briefcaseRoot, manifest);

                return new SyncItemResult
                {
                    Status = SyncStatus.BriefcaseCopiedToSource,
                    Message = "서류가방 파일을 컴퓨터 원본으로 복사했습니다."
                };
            }

            // 양쪽 내용이 서로 다르게 변경됨
            return new SyncItemResult
            {
                Status = SyncStatus.Conflict,
                Message = "양쪽 파일이 모두 변경됐습니다. 덮어쓰기를 보류했습니다."
            };
        }

        private static void CopyFileSafely(
            string sourcePath,
            string destinationPath)
        {
            string directory = Path.GetDirectoryName(destinationPath);

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // 대상 파일과 같은 폴더에 임시 파일 생성
            string tempPath = destinationPath
                + ".nbrief-tmp-"
                + Guid.NewGuid().ToString("N");

            try
            {
                File.Copy(sourcePath, tempPath, false);

                if (File.Exists(destinationPath))
                {
                    // 교체에 실패하면 기존 파일을 임의로 삭제하지 않음
                    File.Replace(tempPath, destinationPath, null);
                }
                else
                {
                    File.Move(tempPath, destinationPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
}
