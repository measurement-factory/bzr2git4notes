using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;

namespace Bzr2Git4Notes
{
    static class LogWriter
    {
        public static void Log(string msg)
        {
            if (!MainClass.Logging)
                return;
            using (StreamWriter writer = new StreamWriter(logFile, true))
                writer.WriteLine(msg);
        }

        static readonly string logFile = String.Format("{0}.log", MainClass.AssemblyName);
    }

    static class FormatHelper
    {
        public static long SecondsSinceEpoch
        {
            get
            {
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return Convert.ToInt64((DateTime.Now - epoch).TotalSeconds);
            }
        }

        public static string timeZoneOffset
        {
            get
            {
                var offset = DateTimeOffset.Now.Offset.TotalHours;
                return String.Format("{0}00", offset.ToString("+00;-00"));
            }
        }
    }

    public class LineReader : BinaryReader
    {
        public LineReader(Stream stream)
			: base(stream, Encoding.UTF8) { }

        static readonly byte[] newline = Encoding.ASCII.GetBytes("\n");

        public string ReadLine()
        {
            List<byte> buffer = new List<byte>(64);
            try
            {
                while (true)
                {
                    byte lastByte = base.ReadByte();
                    if (lastByte == newline[0])
                        return System.Text.UTF8Encoding.Default.GetString(buffer.ToArray());
                    buffer.Add(lastByte);
                }
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }
    }

    // bzr-specific
    // the parsed branch id and nick, provided by 'property branch-nick id nick' command
    [Serializable]
    class BranchName
    {
        public BranchName(string s)
        {
            string[] arr = s.Split(new char [] { ' ' });
            if (arr.Length != 2)
                throw new Exception(String.Format("Invalid property branch-nick format: {0}", s));
            id = int.Parse(arr[0]);
            nick = arr[1];
        }

        public string name { get { return String.Format("{0}-{1}", nick, id); } }
        // the parsed id
        public int id;
        // the parsed nick
        public string nick;
    }

    // the parsed tag information, provided by 'reset' command
    [Serializable]
    class Tag
    {
        public Tag()
        {
            fromId = -1;
            skip = false;
        }
        // the parsed tag_name from 'reset refs/tags/tag_name' command
        public string name;
        // the fromId from 'from :fromId' command, related to this tag
        public int fromId;
        // Whether do not include this tag into output(as belonging to a different branch).
        public bool skip;
    }

    [Serializable]
    class Rename
    {
        public string line;
        public string origName;
        public string newName;
    }

    [Serializable]
    class Renames
    {
        public Renames()
        {
            byOrigDict = new SortedDictionary<string, Rename>();
            byNewDict = new SortedDictionary<string, Rename>();
        }
        public void add(Rename rename)
        {
            byOrigDict.Add(rename.origName, rename);
            byNewDict.Add(rename.newName, rename);
        }
        public void remove(Rename rename)
        {
            byNewDict.Remove(rename.newName);
            byOrigDict.Remove(rename.origName);
        }
        public Rename find(string name)
        {
            if (byOrigDict.ContainsKey(name))
                return byOrigDict[name];
            else if (byNewDict.ContainsKey(name))
                return byNewDict[name];
            return null;
        }
        public void clear()
        {
            byOrigDict.Clear();
            byNewDict.Clear();
        }

        // orig file name
        public SortedDictionary<string, Rename> byOrigDict;
        // new file name
        public SortedDictionary<string, Rename> byNewDict;
    }

    // Represents bzr branch.
    [Serializable]
    class Branch
    {
        public Branch()
        {
            childBranches = new List<Branch>();
            nodeCount = 0;
            isTrunk = false;
        }
        // add direct child
        public void addChild(Branch b)
        {
            if (childBranches == null)
                childBranches = new List<Branch>();
            childBranches.Add(b);
        }
        // recursively get all child branches
        public void allChildren(ref List<Branch> list)
        {
            foreach (var b in childBranches)
            {
                list.Add(b);
                b.allChildren(ref list);
            }
        }

        public string name { get { return end.branchName; } }
        // The first branch node.
        public Node begin;
        // The last branch node. For non-trunk branches
        // it is merged to another(usually parent) branch.
        public Node end;
        // branches directly forked from the current branch
        public List<Branch> childBranches;
        // number of nodes in this branch
        public int nodeCount;
        public bool isTrunk;
    }

    // A node of bzr revision tree.
    [Serializable]
    abstract class Node
    {
        public Node()
        {
            nexts = new List<Node>();
            mergesFrom = new List<Node>();
        }
        // whether one or more branches started from this node
        public bool branched() { return nexts.Count > 1; }

        // whether this node is the result of merge from one or more branches
        public bool merged() { return mergesFrom.Count > 0; }

        // whether this node is the last node for a branch
        public bool finished() { return nexts.Count() == 0; }

        public void addBranch(Branch branch)
        {
            if (derivedBranches == null)
                derivedBranches = new List<Branch>();
            derivedBranches.Add(branch);
        }

        // calculates three-number revision numbers for
        // branches, started from this node.
        public void setDerivedRevisions(int trunkRevNo)
        {
            if (derivedBranches == null)
                return;
            // Branch.begin.id, Branch
            var allBranches = new SortedDictionary<int, Branch>();
            foreach (var branch in derivedBranches)
            {
                var nestedBranches = new List<Branch>();
                nestedBranches.Add(branch);
                branch.allChildren(ref nestedBranches);
                foreach (var nestedBranch in nestedBranches)
                {
                    if (!allBranches.ContainsKey(nestedBranch.begin.id))
                        allBranches.Add(nestedBranch.begin.id, nestedBranch);
                }
            }

            int rank = 1;
            foreach (var branchPair in allBranches)
            {
                int nodePosition = branchPair.Value.nodeCount;
                var node = branchPair.Value.end;

                while (node != branchPair.Value.begin)
                {
                    node.setRevision(trunkRevNo, rank, nodePosition);
                    nodePosition--;
                    node = node.prev;
                }
                node.setRevision(trunkRevNo, rank, nodePosition);
                nodePosition--;
                if (nodePosition != 0) // processed all branch elements
                    throw new Exception(String.Format("expect zero node position, but got {0}", nodePosition));
                rank++;
            }
        }

        public void setRevision(int baseRev, int rank, int branchPos)
        {
            if (rank == 0 && branchPos == 0)
                theRevision = baseRev.ToString();
            else
                theRevision = String.Format("{0}.{1}.{2}", baseRev, rank, branchPos);
        }

        public abstract int id { get; }

        public abstract string branchName { get; }

        public string revision { get { return theRevision; } }

        // List of nodes, having this node as a parent.
        // Contains > 1 elements if this node is a root for one or more branches.
        public List<Node> nexts;
        // Corresponds to id in 'from :id' command from bzr export stream.
        public Node prev;
        // list of nodes merged into this node
        public List<Node> mergesFrom;
        // a node this node was merged to
        public Node mergeTo;
        // a branch this node belongs to
        public Branch branch;
        // Calculated bzr revision, e.g., 'r8881' or 'r8881.2.2'.
        string theRevision;
        // list of branches forked from this node
        List<Branch> derivedBranches;
    }

    // Parsed 'commit' command information from bzr fast-export stream.
    [Serializable]
    class Commit : Node
    {
        public Commit()
        {
            markId = -1;
            fromMarkId = -1;
            mergeMarkId = -1;
            tags = new List<Tag>();
            authors = new List<string>();
        }

        public string Notes(int noteMark, string noteCommitId, string lastImportNoteId)
        {
            StringBuilder messageBuilder = new StringBuilder();
            for (int i = 1; i < authors.Count; ++i)
                messageBuilder.Append(String.Format("Co-Authored-By: {0}\n", authors[i]));
            if (!String.IsNullOrEmpty(bug))
                messageBuilder.Append(String.Format("Fixes: {0}\n", bug));
            if (!string.IsNullOrEmpty(noteCommitId))
                messageBuilder.Append(String.Format("Bzr-Reference: {0}\n", noteCommitId));
            if (messageBuilder.Length == 0)
                return null;

            StringBuilder builder = new StringBuilder();
            builder.Append(String.Format("commit refs/notes/{0}\n", MainClass.NoteNS));
            builder.Append(string.Format("mark :{0}\n", noteMark));
            builder.Append(String.Format("committer {0} <{0}@example.com> {1} {2}\n",
                                         MainClass.AssemblyName, FormatHelper.SecondsSinceEpoch, FormatHelper.timeZoneOffset));
            string notesCommitMessage = "auto-generated git notes from bzr metadata";
            if (!string.IsNullOrEmpty(noteCommitId))
                notesCommitMessage += String.Format(" ({0})", noteCommitId);

            builder.Append(String.Format("data {0}\n", notesCommitMessage.Length));
            builder.Append(notesCommitMessage);
            builder.Append("\n");
            if (!string.IsNullOrEmpty(lastImportNoteId))
                builder.Append(String.Format("from {0}\n", lastImportNoteId));
            builder.Append(String.Format("N inline :{0}\n", markId));



            int messageLength = System.Text.UTF8Encoding.Default.GetByteCount(messageBuilder.ToString());
            builder.Append(String.Format("data {0}\n", messageLength));
            builder.Append(messageBuilder.ToString());
            return builder.ToString();
        }

        public override int id { get { return markId; } }

        public override string branchName { get { return theBranchName.name; } }
        // git notes information in format of '<branch_nick> r<revno>'
        public string bzrReference
        {
            get
            {
                if (MainClass.OnlyPlainRevs && revision.Contains("."))
                    return null;
                string bzrRef = String.Format("{0} r{1}", gitBranchName, revision);
                if (revision.Contains("."))
                    bzrRef = String.Format("{0} from {1}", bzrRef, theBranchName.nick);
                return bzrRef;
            }
        }
        // the parsed id from 'mark :id' command
        public int markId;
        // the parsed fromId from 'from :fromId' command
        public int fromMarkId;
        // the parsed mergeId from 'merge :mergeId' command
        public int mergeMarkId;
        // the parsed committer info(including email and date) from 'committer committer_info' command
        public string committer; // unused
        // the parsed bug_info from 'property bugs bug_info' bzr-specific command
        public string bug;
        // the parsed bzr-specific 'property branch-nick ...' command
        public BranchName theBranchName;
        // The git name this commit will be applied on, specified by
        // 'commit refs/heads/<git_branch_name>' command.
        public string gitBranchName;
        // commit authors from parsed 'author ...' commands
        public List<string> authors;
        public List<Tag> tags; // unused
    }

    // bzr 'fast-export --no-plain' to 'git fast-import' adapter.
    // Extacts some meta-inforation from bzr output (such as authors
    // and revision number) and transforms it into git notes data.
    [Serializable]
    class Adapter
    {
        public Adapter()
        {
            theBranchList = new List<Branch>();
            StoreList = new List<Commit>();
            branchNameDict = new SortedDictionary<string, int>();
            deletedFileLines = new SortedDictionary<string, string>();
            renames = new Renames();
            parseTagsFile();
        }
        #region === public methods ===
        public void Adapt(string inputFname, string outputFname)
        {
            using (outputStream = string.IsNullOrEmpty(outputFname) ? Console.OpenStandardOutput() : File.OpenWrite(outputFname))
            {
                if (MainClass.RestoreContext)
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    using (FileStream fs = new FileStream(MainClass.StoreFile, FileMode.Open))
                    {
                        StoreList = (List<Commit>)formatter.Deserialize(fs);
                        dirs = (HashSet<string>)formatter.Deserialize(fs);
                    }
                    RestoredCount = StoreList.Count;
                    LogWriter.Log(String.Format("Deserialized {0} commits", RestoredCount));
                }

                AdaptInputStream(inputFname);

                if (MainClass.StoreContext)
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    using (FileStream fs = new FileStream(MainClass.StoreFile, FileMode.Create))
                    {
                        formatter.Serialize(fs, StoreList);
                        formatter.Serialize(fs, dirs);
                        LogWriter.Log(String.Format("Serialized {0} commits", StoreList.Count));
                    }
                }
                acquireBranchLevels();
                createBranches();
                ApplyNotes();
            }
        }
        #endregion
        #region === I/O methods ===
        // gets 'bzr fast-export --no-plain' on input, and writes
        // transformed data stream to output in a format, suitable for git fast-import.
        // For example, bzr properties and directory-creating commands are removed
        // from the input stream.
        void AdaptInputStream(string inputFname)
        {
            var stream = string.IsNullOrEmpty(inputFname) ? Console.OpenStandardInput() : File.OpenRead(inputFname);
            using (LineReader inputStream = new LineReader(stream))
            {
                Commit commit = null;
                Tag tag = null;
                string s = null;
                bool skipBlankLine = false;
                while ((s = inputStream.ReadLine()) != null)
                {
                    if (skipBlankLine)
                    {
                        skipBlankLine = false;
                        if (string.IsNullOrEmpty(s))
                            continue;
                    }
                    if (s.StartsWith("feature "))
                    {
                        // skip branch-specific
                    }
                    else if (s.StartsWith("reset "))
                    {
                        if (commit != null)
                        {
                            addCommit(commit);
                            commit = null;
                        }
                        if (s.StartsWith("reset refs/tags/"))
                        {
                            tag = new Tag();
                            tag.name = s.Substring(("reset refs/tags/").Length);
                            if (requiredTags != null && !requiredTags.Contains(tag.name))
                            {
                                tag.skip = true;
                                skipBlankLine = true;
                                LogWriter.Log(String.Format("Skipping tag {0}", tag.name));
                                continue;
                            }
                        }
                        WriteLine(s);
                    }
                    else if (s.StartsWith("commit "))
                    {
                        if (commit != null)
                            addCommit(commit);
                        commit = new Commit();
                        commit.gitBranchName = Remainder(s, "commit refs/heads/");
                        WriteLine(s);
                    }
                    else if (s.StartsWith("mark :"))
                    {
                        ThrowIfNull(commit, s);
                        commit.markId = RemainderToInt(s, "mark :");
                        WriteLine(s);
                    }
                    else if (s.StartsWith("committer "))
                    {
                        ThrowIfNull(commit, s);
                        commit.committer = Remainder(s, "committer ");
                        WriteLine(s);
                    }
                    else if (s.StartsWith("author "))
                    {
                        ThrowIfNull(commit, s);
                        var remainder = Remainder(s, "author ");
                        var author = remainder.Substring(0, remainder.LastIndexOf('>') + 1);
                        commit.authors.Add(author);
                        if (commit.authors.Count == 1)
                            WriteLine(s);
                    }
                    else if (s.StartsWith("data "))
                    {
                        int dataLength = RemainderToInt(s, "data ");
                        WriteLine(s);
                        var buffer = ReadDataBlock(inputStream, dataLength);
                        outputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (s.StartsWith("from :"))
                    {
                        int id = int.Parse(s.Substring(("from :").Length));
                        if (StoreList.Count < id)
                            throw new Exception(String.Format("'from' points to non-existing id: {0}", id));
                        var fromCommit = StoreList[id - 1];
                        if (tag != null)
                        {
                            if (tag.skip)
                            {
                                skipBlankLine = true;
                                continue;
                            }
                            tag.fromId = id;
                            fromCommit.tags.Add(tag);
                            tag = null;
                        }
                        else
                        {
                            ThrowIfNull(commit, s);
                            commit.fromMarkId = id;
                            commit.prev = fromCommit;
                            fromCommit.nexts.Add(commit);
                        }
                        WriteLine(s);
                    }
                    else if (s.StartsWith("merge :"))
                    {
                        ThrowIfNull(commit, s);
                        commit.mergeMarkId = RemainderToInt(s, "merge :");
                        var mergeFromCommit = StoreList[commit.mergeMarkId - 1];
                        commit.mergesFrom.Add(mergeFromCommit);
                        mergeFromCommit.mergeTo = commit;
                        WriteLine(s);
                    }
                    else if (s.StartsWith("property rebase-of "))
                        continue;
                    else if (s.StartsWith("property bugs "))
                    {
                        ThrowIfNull(commit, s);

                        int bugLength = ParseBugLength(s);
                        int bugHeadLength = ("property bugs " + bugLength.ToString() + " ").Length;
                        commit.bug = s.Substring(bugHeadLength);
                        if (s.Length - bugHeadLength < bugLength)
                            commit.bug += "\n" + ReadDataBlockAsString(inputStream, bugLength - (s.Length - bugHeadLength));
                    }
                    else if (s.StartsWith("property branch-nick "))
                    {
                        ThrowIfNull(commit, s);
                        commit.theBranchName = new BranchName(Remainder(s, "property branch-nick "));
                        // skip property
                    }
                    else if (s.StartsWith("R "))
                    {
                        ThrowIfNull(commit, s);
                        string origName = s.Substring(2, s.IndexOf(" ", 2) - 2);
                        string newName = s.Substring(s.IndexOf(" ", 2) + 1);
                        // If directory, track the new name.
                        if (dirs.Contains(origName))
                            dirs.Add(newName);
                        else
                        {
                            Rename rename = new Rename();
                            rename.origName = origName;
                            rename.newName = newName;
                            rename.line = s;
                            renames.add(rename);
                        }
                    }
                    else if (s.StartsWith("D "))
                    {
                        ThrowIfNull(commit, s);
                        var name = Remainder(s, "D ");
                        if (!dirs.Contains(name))
                        {
                            WriteLine(s);
                            deletedFileLines.Add(name, s);
                        }
                    }
                    else if (s.StartsWith("M "))
                    {
                        // "M 040000 - dir_name"
                        if (s.StartsWith("M 040000"))
                            dirs.Add(s.Substring(11));
                        else
                        {
                            string name = s.Substring(s.LastIndexOf(" ") + 1);
                            // rename first, then modify
                            Rename rename = renames.find(name);
                            if (rename != null)
                            {
                                // problematic r10084:
                                // R helpers/negotiate_auth/squid_kerb_auth/config.test helpers/negotiate_auth/kerberos/config.test
                                // problematic r11203: src/mgr/Response.h
                                if (!deletedFileLines.ContainsKey(rename.origName))
                                    WriteLine(rename.line);
                                renames.remove(rename);
                            }
                            WriteLine(s);
                        }
                    }
                    else if (s.StartsWith("data "))
                    {
                        int dataLength = RemainderToInt(s, "data ");
                        WriteLine(s);
                        var data = ReadDataBlockAsString(inputStream, dataLength);
                        WriteLine(data);
                    }
                    else
                    {
                        // write as-is by default
                        WriteLine(s);
                    }
                }
                if (commit != null)
                    addCommit(commit);
            }
        }
        // write note information for all commits
        void ApplyNotes()
        {
            var start = RestoredCount > 0 ? RestoredCount : 0;
            LogWriter.Log(String.Format("Applying notes, start = {0}", start));
            bool attachtoPrevNotes = false;
            int markId = StoreList[StoreList.Count - 1].markId + 1;
            if (!String.IsNullOrEmpty(MainClass.LastNoteId))
            {
                attachtoPrevNotes = true;
                int parsed;
                if (MainClass.LastNoteId.StartsWith(":") &&
                    int.TryParse(MainClass.LastNoteId.Substring(1), out parsed))
                    markId = parsed + 1;
            }
            for (int i = start; i < StoreList.Count; ++i)
            {
                var commit = StoreList[i];
                var notes = commit.Notes(markId, commit.bzrReference, attachtoPrevNotes ? MainClass.LastNoteId : "");
                if (!string.IsNullOrEmpty(notes))
                {
                    WriteLine(notes);
                    attachtoPrevNotes = false;
                    markId++;
                }
            }
        }
        #endregion
        #region === I/O helper methods ===
        static string ReadDataBlockAsString(LineReader inputStream, int dataLength)
        {
            return System.Text.UTF8Encoding.Default.GetString(inputStream.ReadBytes(dataLength));
        }

        static byte[] ReadDataBlock(LineReader inputStream, int dataLength)
        {
            byte[] buffer = inputStream.ReadBytes(dataLength);
            return buffer;
        }

        static void WriteLine(string s)
        {
            byte[] bytes = System.Text.UTF8Encoding.Default.GetBytes(s);
            outputStream.Write(bytes, 0, bytes.Length);
            outputStream.Write(Newline, 0, Newline.Length);
        }
        #endregion
        #region === revision calculation methods ===
        void acquireBranchLevels()
        {
            // fill all trunk names first (e.g., 'HEAD', 'trunk', 'TRUNK', etc.)
            for (Node node = StoreList.Last(); node != null; node = node.prev)
            {
                if (!branchNameDict.ContainsKey(node.branchName))
                    branchNameDict.Add(node.branchName, 1);
            }

            foreach (var node in StoreList)
            {
                if (node.branched())
                {
                    int curLevel = branchNameDict[node.branchName];
                    // (id, branch name). The branch with a bigger(i.e., more recent) 'id'
                    // will get a higher level.
                    SortedDictionary<int, string> idDict = new SortedDictionary<int, string>();
                    foreach (var aBranchNode in node.nexts)
                    {
                        if (aBranchNode.branchName != node.branchName)
                            idDict.Add(aBranchNode.id, aBranchNode.branchName);
                    }
                    curLevel++;
                    foreach (var p in idDict)
                    {
                        // Skip processing branches with duplicated names (for
                        // now), because there is not enough information for
                        // this yet. There are many such 'duplicated' branches
                        // in Squid bzr history, e.g., several auxiliary 'trunk'
                        // branches were forked from the genuine 'trunk'. These
                        // special cases are processed later, at
                        // TraverseBranches().
                        if (branchNameDict.ContainsKey(p.Value))
                        {
                            LogWriter.Log(String.Format("duplicated branch {0}", p.Value));
                            continue;
                        }
                        branchNameDict.Add(p.Value, curLevel);
                        curLevel++;
                    }
                }
                else
                {
                    // skip the first node
                    if (node.prev == null)
                        continue;

                    // There are several strange situations, when a branch name
                    // was changed between two adjacent commits.  For example,
                    // it changed from 'squid-autoconf-refactor' to
                    // 'autoconf-refactor' between r10147.1.21..r10147.1.22 and
                    // from 'memcache' to 'memcache-controls' between
                    // r9859.1.11..r9859.1.12.  Adjust these branch names to
                    // correspond to the same level.
                    var prevCommit = node.prev;
                    if (node.branchName != prevCommit.branchName)
                    {
                        if (branchNameDict.ContainsKey(prevCommit.branchName) &&
                            !branchNameDict.ContainsKey(node.branchName))
                        {
                            LogWriter.Log(String.Format("renamed branch from {0} to {1}", prevCommit.branchName, node.branchName));
                            int prevLevel = branchNameDict[prevCommit.branchName];
                            // can't have level == 1 which is for trunk names
                            prevLevel = prevLevel <= 2 ? 2 : prevLevel;
                            branchNameDict.Add(node.branchName, prevLevel);
                        }
                    }
                }
            }
        }

        void createBranches()
        {
            Branch trunk = new Branch();
            trunk.end = StoreList.Last();
            trunk.begin = StoreList.First();
            StoreList.First().branch = trunk;
            StoreList.Last().branch = trunk;
            trunk.nodeCount = 1;
            trunk.isTrunk = true;

            var branchList = new List<Branch> { trunk };
            var processedNodes = new HashSet<int>() { StoreList.First().id };
            int minLevel = 1;

            traverseBranches(branchList, ref processedNodes, ref minLevel);

            int revNo = trunk.nodeCount;
            for (var node = trunk.end; node != null; node = node.prev)
            {
                node.setRevision(revNo, 0, 0);
                if (node.branched())
                    node.setDerivedRevisions(revNo);
                revNo--;
            }

            // foreach (var c in StoreList)
            // {
            //     LogWriter.Log(String.Format("{0} {1}", c.markId, c.revision));
            // }
        }

        // Walks over all nodes creating branches at merge points. Each node
        // gets its branch. Branches are built into hierarchy.
        void traverseBranches(List<Branch> branchList, ref HashSet<int> processedNodes, ref int minLevel)
        {
            if (branchList.Count == 0)
                return;
            var nextLevelBranches = new List<Branch>();
            branchList.Reverse(); // process early-merged branches first
            SortedDictionary<int, List<Branch>> levelDict = new SortedDictionary<int, List<Branch>>();
            foreach (var branch in branchList)
            {
                int level = branchNameDict[branch.name] < minLevel ? minLevel : branchNameDict[branch.name];
                if (!levelDict.ContainsKey(level))
                    levelDict.Add(level, new List<Branch>());
                levelDict[level].Add(branch);
            }
            foreach (var pair in levelDict)
            {
                foreach (var branch in pair.Value)
                {
                    Node node = branch.end;
                    Node prevNode = node;
                    while (!processedNodes.Contains(node.id))
                    {
                        processedNodes.Add(node.id);
                        branch.nodeCount++;
                        if (node.merged())
                        {
                            foreach (var mergedFrom in node.mergesFrom)
                            {
                                // ignore merges from parent branches
                                // (e.g., merge from trunk to a feature branch)
                                if (processedNodes.Contains(mergedFrom.id))
                                    continue;
                                // process only last merge from a branch
                                if (!mergedFrom.finished())
                                    continue;
                                var newBranch = new Branch();
                                newBranch.end = mergedFrom;
                                nextLevelBranches.Add(newBranch);
                            }
                        }
                        node.branch = branch;
                        prevNode = node;
                        node = node.prev;
                    }
                    if (!branch.isTrunk)
                    {
                        if (!node.branched())
                            throw new Exception(String.Format("Must be branched node, id = {0}", node.id));
                        node.addBranch(branch);
                        branch.begin = prevNode;
                        node.branch.addChild(branch);
                    }
                    theBranchList.Add(branch);
                }
            }
            minLevel++;
            traverseBranches(nextLevelBranches, ref processedNodes, ref minLevel);
        }
        #endregion
        #region === helper parsing methods ===

        static int ParseBugLength(string s)
        {
            var match = bugLengthRegex.Match(s);
            if (!match.Success)
                throw new Exception(String.Format("Can't parse bug length for {0}", s));
            return int.Parse(match.Groups[1].Value);
        }

        static string Remainder(string line, string start)
        {
            return line.Substring(start.Length);
        }

        static int RemainderToInt(string line, string start)
        {
            return int.Parse(Remainder(line, start));
        }

        static void ThrowIfNull(Commit c, string context)
        {
            if (c == null)
                throw new Exception(String.Format("commit = null for {0}", context));
        }
        #endregion

        void addCommit(Commit commit)
        {
            foreach (var pair in renames.byOrigDict)
            {
                var rename = pair.Value;
                if (!deletedFileLines.ContainsKey(rename.origName))
                    WriteLine(rename.line);
            }
            renames.clear();
            deletedFileLines.Clear();
            StoreList.Add(commit);
        }

        void parseTagsFile()
        {
            if (!string.IsNullOrEmpty(MainClass.TagsFile))
            {
                requiredTags = new List<string>();
                var lines = File.ReadLines(MainClass.TagsFile);
                foreach (var l in lines)
                {
                    if (!string.IsNullOrEmpty(l))
                        requiredTags.Add(l.Trim());
                }
            }
        }

        #region === private and static data ===
        private static HashSet<string> dirs = new HashSet<string>();
        static readonly byte[] Newline = System.Text.UTF8Encoding.Default.GetBytes("\n");
        static readonly Regex bugLengthRegex = new Regex("property bugs ([0-9]+)", RegexOptions.Compiled);
        static Stream outputStream;
        List<Branch> theBranchList;
        // All parsed bzr export 'commit' commands.
        List<Commit> StoreList;
        // Contains pairs (branch name, level), where 'level'
        // stands for branch nesting level. It is '1' for trunk branch names
        // (e.g., 'trunk' or 'HEAD'), and > 1 for other branches.
        // This dictionary is advisory since it does not contain full required information
        // about available branches. E.g., it misses nested branches with coincident names
        // (e.g.,'trunk').
        SortedDictionary<string, int> branchNameDict;

        // These three dictionaries help handling complex
        // scenarious of renaming/deleting/modifying a single file
        // within a specific commit. For example, we should not rename/move
        // file (when its direcotry gets renamed) if it has been deleted yet.
        // name, line
        SortedDictionary<string, string> deletedFileLines;

        Renames renames;

        // We need to import only these, branch-specific tags.
        List<string> requiredTags;

        int RestoredCount;
        #endregion
    }

    class MainClass
    {
        public static string InputFile;
        public static string OutputFile;
        public static bool StoreContext = false;
        public static bool RestoreContext = false;
        public static string StoreFile = String.Format("{0}.bin", AssemblyName);
        public static string NoteNS = "commits";
        public static bool Logging = false;

        // Last note identificator of the existing git repository,
        // i.e., what the first new note will get in its 'from'.
        // This parameter is required to sustein continius notes chain when importing
        // multiple branches.
        // It can be either:
        // 1. Name of the existing notes 'branch', i.e. 'notes/commits'.
        // 2. The last imported note SHA.
        // 3. The last imported note mark id in the ":id" format (can be taken
        //    from previously generated git marks file).
        public static string LastNoteId;
        // A text file with tag name list, required for current branch.
        // All other tags will be skipped.
        public static string TagsFile;
        public static bool OnlyPlainRevs = false;

        public static string AssemblyName
        {
            get { return typeof(MainClass).Assembly.GetName().Name; }
        }

        public static void ParseOptions(string[] args)
        {
            foreach (var option in args)
            {
                if (option.StartsWith("--input="))
                    InputFile = option.Substring("--input=".Length);
                else if (option.StartsWith("--output="))
                    OutputFile = option.Substring("--output=".Length);
                else if (option.StartsWith("--store-context"))
                    StoreContext = true;
                else if (option.StartsWith("--restore-context"))
                    RestoreContext = true;
                else if (option.StartsWith("--note-ns="))
                    NoteNS = option.Substring("--note-ns=".Length);
                else if (option.StartsWith("--logging"))
                    Logging = true;
                else if (option.StartsWith("--last-note-id="))
                    LastNoteId = option.Substring("--last-note-id=".Length);
                else if (option.StartsWith("--tags-file="))
                    TagsFile = option.Substring("--tags-file=".Length);
                else if (option.StartsWith("--only-plain-revs"))
                    OnlyPlainRevs = true;
                else
                    throw new Exception(String.Format("Unknown option {0}", option));
            }
        }

        public static int Main(string[] args)
        {
            try
            {
                ParseOptions(args);
                LogWriter.Log(String.Format("Started at {0}", DateTime.Now));
                Adapter adapter = new Adapter();
                adapter.Adapt(InputFile, OutputFile);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(String.Format("Terminated with exception: {0}\n{1}",
                                                      ex.Message, ex.StackTrace));
                LogWriter.Log(String.Format("Terminated with exception at {0} : {1}\n{2}",
                                            DateTime.Now, ex.Message, ex.StackTrace));
                return 1;
            }
            LogWriter.Log(String.Format("Finished successfully at {0}", DateTime.Now));
            return 0;
        }
    }
}

