﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThunderKit.Common.Package;
using ThunderKit.Core.Editor;
using ThunderKit.Core.Manifests;
using ThunderKit.Core.Manifests.Datum;
using UnityEditor;
using UnityEngine;

namespace ThunderKit.Core.Data
{
    public abstract class PackageSource : ScriptableObject, IEquatable<PackageSource>
    {
        static Dictionary<string, List<PackageSource>> sourceGroups;
        public static Dictionary<string, List<PackageSource>> SourceGroups
        {
            get
            {
                if (sourceGroups == null)
                {
                    sourceGroups = new Dictionary<string, List<PackageSource>>();
                    var packageSources = AssetDatabase.FindAssets("t:PackageSource", new string[] { "Assets", "Packages" })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(AssetDatabase.LoadAssetAtPath<PackageSource>);
                    foreach (var packageSource in packageSources)
                    {
                        if (!sourceGroups.ContainsKey(packageSource.SourceGroup))
                            sourceGroups[packageSource.SourceGroup] = new List<PackageSource>();

                        if (!sourceGroups[packageSource.SourceGroup].Contains(packageSource))
                            sourceGroups[packageSource.SourceGroup].Add(packageSource);
                    }

                }
                return sourceGroups;
            }
        }

        public DateTime lastUpdateTime;
        public abstract string Name { get; }
        public abstract string SourceGroup { get; }

        public List<PackageGroup> Packages;

        private Dictionary<string, HashSet<string>> dependencyMap;
        private Dictionary<string, PackageGroup> groupMap;

        /// <summary>
        /// Generates a new PackageGroup for this PackageSource
        /// </summary>
        /// <param name="author"></param>
        /// <param name="name"></param>
        /// <param name="description"></param>
        /// <param name="dependencyId">DependencyId for PackageGroup, this is used for mapping dependencies</param>
        /// <param name="tags"></param>
        /// <param name="versions">Collection of version numbers, DependencyIds and dependencies as an array of versioned DependencyIds</param>
        protected void AddPackageGroup(string author, string name, string description, string dependencyId, string[] tags, IEnumerable<(string version, string versionDependencyId, string[] dependencies)> versions)
        {
            if (groupMap == null) groupMap = new Dictionary<string, PackageGroup>();
            if (dependencyMap == null) dependencyMap = new Dictionary<string, HashSet<string>>();
            if (Packages == null) Packages = new List<PackageGroup>();
            var group = CreateInstance<PackageGroup>();

            group.Author = author;
            group.name = group.PackageName = name;
            group.Description = description;
            group.DependencyId = dependencyId;
            group.Tags = tags;
            group.Source = this;
            groupMap[dependencyId] = group;

            group.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
            AssetDatabase.AddObjectToAsset(group, this);

            var versionData = versions.ToArray();
            group.Versions = new PackageVersion[versionData.Length];
            for (int i = 0; i < versionData.Length; i++)
            {
                var (version, versionDependencyId, dependencies) = versionData[i];

                var packageVersion = CreateInstance<PackageVersion>();
                packageVersion.name = packageVersion.dependencyId = versionDependencyId;
                packageVersion.group = group;
                packageVersion.version = version;
                packageVersion.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
                AssetDatabase.AddObjectToAsset(packageVersion, group);
                group.Versions[i] = packageVersion;

                if (!dependencyMap.ContainsKey(packageVersion.dependencyId))
                    dependencyMap[packageVersion.dependencyId] = new HashSet<string>();

                foreach (var depDepId in dependencies)
                    dependencyMap[packageVersion.dependencyId].Add(depDepId);
            }

            Packages.Add(group);
        }

        /// <summary>
        /// Loads data from data source into the current PackageSource via AddPackageGroup
        /// </summary>
        protected abstract void OnLoadPackages();

        /// <summary>
        /// Provides a conversion of versioned dependencyIds to group dependencyIds
        /// </summary>
        /// <param name="dependencyId">Versioned Dependency Id</param>
        /// <returns>Group DependencyId which dependencyId is mapped to</returns>
        protected abstract string VersionIdToGroupId(string dependencyId);
        public void LoadPackages()
        {
            OnLoadPackages();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var versionMap = Packages.Where(pkgGrp => pkgGrp?.Versions != null).SelectMany(pkgGrp => pkgGrp.Versions.Select(pkgVer => (pkgGrp, pkgVer))).ToDictionary(ver => ver.pkgVer.dependencyId);

            foreach (var packageGroup in Packages)
            {
                var groupName = packageGroup.PackageName;
                foreach (var version in packageGroup.Versions)
                {
                    var dependencies = dependencyMap[version.name].ToArray();
                    version.dependencies = new PackageVersion[dependencies.Length];
                    for (int i = 0; i < dependencies.Length; i++)
                    {
                        string dependencyId = dependencies[i];
                        string groupId = VersionIdToGroupId(dependencyId);
                        if (versionMap.ContainsKey(dependencyId))
                        {
                            version.dependencies[i] = versionMap[dependencyId].pkgVer;
                        }
                        else if (groupMap.ContainsKey(groupId))
                        {
                            version.dependencies[i] = groupMap[groupId]["latest"];
                        }
                    }
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        IEnumerable<PackageVersion> EnumerateDependencies(PackageVersion package)
        {
            foreach (var dependency in package.dependencies)
            {
                foreach (var subDependency in EnumerateDependencies(dependency))
                    yield return subDependency;

                yield return dependency;
            }
        }

        public async Task InstallPackage(PackageGroup group, string version)
        {
            var package = group[version];

            var installSet = EnumerateDependencies(package).Distinct().Append(package).Where(dep => !dep.group.Installed).ToArray();

            #region Multi-Source support?
            //if (SourceGroups.ContainsKey(SourceGroup))
            //{
            //var sourceGroup = SourceGroups[SourceGroup];
            //var sourcePackages = sourceGroup.ToDictionary(source => source, source => source.Packages);

            //var inverseDependencyMap = new Dictionary<PackageGroup, List<PackageGroup>>();
            //nothing depends on the package being installed by this function, however keys will be used to determine when packages will be installed by the content of their values
            //inverseDependencyMap[group] = new List<PackageGroup>(0);

            //var pendingDependencies = new HashSet<string>(group[version].dependencies);

            //var finalDependencies = new List<(PackageGroup, PackageVersion)>();

            //var currentDependant = group;
            //while (pendingDependencies.Any())
            //{
            //    foreach (var source in sourceGroup)
            //    {
            //        var packages = sourcePackages[source];
            //        var dependencies = packages.Where(pkg => pkg.VersionIds.Any(pendingDependencies.Contains));

            //        foreach (var dep in dependencies)
            //        {
            //            if (!inverseDependencyMap.ContainsKey(dep)) inverseDependencyMap[dep] = new List<PackageGroup>();
            //            var depVersion = dep.Versions.First(pv => pendingDependencies.Contains(pv.dependencyId));
            //            pendingDependencies.Remove(depVersion.dependencyId);



            //            //inverseDependencyMap[dep]
            //        }

            //        //foreach (var (packageGroup, packageVersions) in dependencies)
            //        //    foreach (var packageVersion in packageVersions)
            //        //    {
            //        //        pendingDependencies.Remove(packageVersion.dependencyId);
            //        //        if (finalDependencies.Contains((packageGroup, packageVersion))) continue;
            //        //        finalDependencies.Add((packageGroup, packageVersion));

            //        //        foreach (var nestedDependency in packageVersion.dependencies)
            //        //            pendingDependencies.Add(nestedDependency);
            //        //    }
            //    }
            //}

            //var builder = new StringBuilder();
            //builder.AppendLine($"Found {finalDependencies.Count} dependencies");
            //foreach (var (pg, pv) in finalDependencies)
            //    builder.AppendLine(pv.dependencyId);

            //Debug.Log(builder.ToString());

            //foreach (var (pg, pv) in finalDependencies)
            //{
            //    //This will cause repeated installation of dependencies
            //    if (Directory.Exists(pg.PackageDirectory)) Directory.Delete(pg.PackageDirectory);
            //    Directory
            //        .CreateDirectory(pg.PackageDirectory);

            //    await pg.Source.InstallPackageFiles(pg[version], pg.PackageDirectory);

            //    EstablishPackage(pg, version);

            //}
            //}
            #endregion

            foreach (var installable in installSet)
            {
                //This will cause repeated installation of dependencies
                if (Directory.Exists(installable.group.PackageDirectory)) Directory.Delete(installable.group.PackageDirectory, true);
                Directory
                    .CreateDirectory(installable.group.PackageDirectory);

                await installable.group.Source.OnInstallPackageFiles(installable, installable.group.PackageDirectory);

                foreach (var assemblyPath in Directory.EnumerateFiles(installable.group.PackageDirectory, "*.dll"))
                    PackageHelper.WriteAssemblyMetaData(assemblyPath, $"{assemblyPath}.meta");
            }
            var tempRoot = Path.Combine("Assets", "ThunderKitSettings", "Temp");
            Directory.CreateDirectory(tempRoot);
            foreach (var installable in installSet)
            {
                var assetTempPath = Path.Combine(tempRoot, $"{installable.group.PackageName}.asset");

                var identity = CreateInstance<ManifestIdentity>();
                identity.name = nameof(ManifestIdentity);
                identity.Author = installable.group.Author;
                identity.Description = installable.group.Description;
                identity.Name = installable.group.PackageName;
                identity.Version = version;
                var manifest = ScriptableHelper.EnsureAsset<Manifest>(assetTempPath);
                manifest.InsertElement(identity, 0);
            }
            foreach (var installable in installSet)
            {
                var assetTempPath = Path.Combine(tempRoot, $"{installable.group.PackageName}.asset");
                var identity = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetTempPath).OfType<ManifestIdentity>().First();
                identity.Dependencies = new Manifest[installable.dependencies.Length];
                for (int i = 0; i < installable.dependencies.Length; i++)
                {
                    var installableDependency = installable.dependencies[i];
                    var dependencyAssetTempPath = Path.Combine(tempRoot, $"{installableDependency.group.PackageName}.asset");
                    var manifest = AssetDatabase.LoadAssetAtPath<Manifest>(dependencyAssetTempPath);
                    if (!manifest)
                    {
                        string[] manifests = AssetDatabase.FindAssets($"t:{nameof(Manifest)} {installableDependency.group.PackageName}",
                                                              new string[] { "Packages" });

                        manifest = manifests.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Manifest>).First();
                    }
                    identity.Dependencies[i] = manifest;
                }
                EditorUtility.SetDirty(identity);
            }
            AssetDatabase.SaveAssets();
            foreach (var installable in installSet)
            {
                var assetTempPath = Path.Combine(tempRoot, $"{installable.group.PackageName}.asset");
                var assetMetaTempPath = Path.Combine(tempRoot, $"{installable.group.PackageName}.asset.meta");
                var assetPackagePath = Path.Combine(installable.group.PackageDirectory, $"{installable.group.PackageName}.asset");
                var assetMetaPackagePath = Path.Combine(installable.group.PackageDirectory, $"{installable.group.PackageName}.asset.meta");

                var fileData = File.ReadAllText(assetTempPath);
                var metafileData = File.ReadAllText(assetMetaTempPath);
                AssetDatabase.DeleteAsset(assetTempPath);

                File.WriteAllText(assetPackagePath, fileData);
                File.WriteAllText(assetMetaPackagePath, metafileData);
            }
            foreach (var installable in installSet)
                PackageHelper.GeneratePackageManifest(
                    installable.dependencyId.ToLower(), installable.group.PackageDirectory,
                    installable.group.PackageName, installable.group.Author,
                    installable.version,
                    installable.group.Description);

            Directory.Delete(tempRoot);
            File.Delete($"{tempRoot}.meta");

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Executes the downloading, unpacking, and placing of package files.  Files
        /// </summary>
        /// <param name="version">The version of the Package which should be installed</param>
        /// <param name="packageDirectory">Root directory which files should be extracted into</param>
        /// <returns></returns>
        public abstract Task OnInstallPackageFiles(PackageVersion version, string packageDirectory);


        public override bool Equals(object obj)
        {
            return Equals(obj as PackageSource);
        }

        public bool Equals(PackageSource other)
        {
            return other != null &&
                   base.Equals(other) &&
                   Name == other.Name &&
                   SourceGroup == other.SourceGroup;
        }

        public override int GetHashCode()
        {
            int hashCode = 1502236599;
            hashCode = hashCode * -1521134295 + base.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(SourceGroup);
            return hashCode;
        }

        public static bool operator ==(PackageSource left, PackageSource right)
        {
            return EqualityComparer<PackageSource>.Default.Equals(left, right);
        }

        public static bool operator !=(PackageSource left, PackageSource right)
        {
            return !(left == right);
        }
    }
}