using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace Bzr2Git4Notes
{
    static class LogWriter
    {
        static LogWriter()
        {
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }

        public static void Log(string msg)
        {
            using (StreamWriter writer = new StreamWriter(logFile, true))
            {
                writer.WriteLine(msg);
            }
        }

        static readonly string logFile = "Bzr2Git4Notes.log";
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
			: base(stream, Encoding.UTF8)
        {
        }

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
    // the parsed author name and email
    class Author
    {
        public Author(string s)
        {
            string[] arr = s.Split(new char [] { ' ' });
            if (arr.Length < 2)
                throw new Exception(String.Format("invalid author format: {0}", s));
            name = arr[0];
            email = arr[1];
        }
        // author name
        public string name;
        // author email
        public string email;
    }
    // the parsed tag information, provided by 'reset' command
    class Tag
    {
        // the parsed tag_name from 'reset refs/tags/tag_name' command
        public string name;
        // the fromId from 'from :fromId' command, related to this tag
        public int fromId;
    }
    // unused
    class Rename
    {
        public string OrigName { get; set; }

        public string NewName { get; set; }
    }
    // Represents bzr branch.
    class Branch
    {
        public Branch()
        {
            childBranches = new List<Branch>();
            rank = -1;
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
        public int rank;
        public int nodeCount;
        public bool isTrunk;
    }
    // A node of bzr revisions tree.
    abstract class Node
    {
        public Node()
        {
            nexts = new List<Node>();
            mergesFrom = new List<Node>();
        }
        // whether one or more branches started from this node
        public bool branched()
        {
            return nexts.Count > 1;
        }
        // whether this node is the result of merge from one or more branches
        public bool merged()
        {
            return mergesFrom.Count > 0;
        }
        // whether this node is the last node for a branch
        public bool finished()
        {
            return nexts.Count() == 0;
        }

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
                branchPair.Value.rank = rank;
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

        public List<Node> nexts;
        public Node prev;
        public List<Node> mergesFrom;
        public Node mergeTo;
        public Branch branch;
        // a branch this node belongs to
        // Calculated bzr revision, e.g., 'r8881' or 'r8881.2.2'.
        string theRevision;
        List<Branch> derivedBranches;
    }
    // Parsed 'commit' command information from bzr fast-export stream.
    class Commit : Node
    {
        public Commit()
        {
            markId = -1;
            fromMarkId = -1;
            mergeMarkId = -1;
            renames = new List<Rename>();
            deletes = new List<string>();
            dirs = new List<string>();
            tags = new List<Tag>();
            authors = new List<Author>();
        }

        public string Notes(int noteMark, string noteCommitId)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("commit refs/notes/commits\n");
            builder.Append(string.Format("mark :{0}\n", noteMark));
            builder.Append(String.Format("committer ImportBot <ImportBot@example.com> {0} {1}\n", FormatHelper.SecondsSinceEpoch, FormatHelper.timeZoneOffset));
            builder.Append(String.Format("data {0}\n", notesCommitMessage.Length));
            builder.Append(notesCommitMessage);
            builder.Append("\n");
            builder.Append(String.Format("N inline :{0}\n", markId));

            StringBuilder messageBuilder = new StringBuilder();
            for (int i = 1; i < authors.Count; ++i)
                messageBuilder.Append(String.Format("Co-Authored-By: {0} {1}\n", authors[i].name, authors[i].email));
            if (!String.IsNullOrEmpty(bug))
                messageBuilder.Append(String.Format("Fixes: {0}\n", bug));
            messageBuilder.Append(String.Format("Bzr-Reference: {0}\n", noteCommitId));

            int messageLength = System.Text.UTF8Encoding.Default.GetByteCount(messageBuilder.ToString());
            builder.Append(String.Format("data {0}\n", messageLength));
            builder.Append(messageBuilder.ToString());
            return builder.ToString();
        }

        public override int id { get { return markId; } }

        public override string branchName { get { return theBranchName.name; } }
        // git notes information in format of '<branch_nick> r<revno>'
        public string bzrReference { get { return String.Format("{0} r{1}", theBranchName.nick, revision); } }
        // the parsed id from 'mark :id' command
        public int markId;
        // the parsed fromId from 'from :fromId' command
        public int fromMarkId;
        // the parsed mergeId from 'merge :mergeId' command
        public int mergeMarkId;
        // the parsed committer info(including email and date) from 'committer committer_info' command
        public string committer;
        // unused
        // the parsed bug_info from 'property bugs bug_info' bzr-specific command
        public string bug;
        // the parsed bzr-specific 'property branch-nick ...' command
        public BranchName theBranchName;
        // commit authors from parsed 'author ...' commands
        public List<Author> authors;
        static readonly string notesCommitMessage = "generating notes from bzr metadata";
        public List<Rename> renames;
        // unused
        public List<string> deletes;
        // unused
        public List<string> dirs;
        // unused   
        public List<Tag> tags;
        // unused
    }
    // bzr 'fast-export --no-plain' to 'git fast-import' adapter.
    // Extacts some meta-inforation from bzr output (such as authors
    // and revision number) and transforms it into git notes data.
    class Adapter
    {
        public Adapter()
        {
            StoreList = new List<Commit>();			
            theBranchList = new List<Branch>();
            branchNameDict = new SortedDictionary<string, int>();
        }
        #region === public methods ===
        public void Adapt(string inputFname, string outputFname)
        {
            using (outputStream = string.IsNullOrEmpty(outputFname) ? Console.OpenStandardOutput() : File.OpenWrite(outputFname))
            {
                AdaptInputStream(inputFname);
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
                while ((s = inputStream.ReadLine()) != null)
                {			
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
                        }
                        WriteLine(s);
                    }
                    else if (s.StartsWith("commit "))
                    {
                        if (commit != null)
                            addCommit(commit);								
                        commit = new Commit();
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
                        commit.authors.Add(new Author(Remainder(s, "author ")));
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
                            rename.OrigName = origName;
                            rename.NewName = newName;
                            commit.renames.Add(rename);
                            WriteLine(s);
                        }						
                    }
                    else if (s.StartsWith("D "))
                    {
                        ThrowIfNull(commit, s);
                        var name = Remainder(s, "D ");
                        if (!dirs.Contains(name))                            
                            WriteLine(s);                             
                    }
                    else if (s.StartsWith("M "))
                    {
                        // "M 040000 - dir_name"
                        if (s.StartsWith("M 040000"))
                            dirs.Add(s.Substring(11));
                        else
                            WriteLine(s);
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
            int markId = StoreList[StoreList.Count - 1].markId + 1;
            foreach (var commit in StoreList)
            {
                WriteLine(commit.Notes(markId, commit.bzrReference));
                markId++;
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
                        // Skip processing branches with repeated names, because there is not enough
                        // information for this yet. This is not unusual for Squid bzr repository,
                        // e.g., are several auxillary 'trunk' branches forked from the genuine 'trunk' 
                        // branch. Such branches are processed later, at TraverseBranches().
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
                    // There are several strange situations, when a branch name was changed
                    // between two adjacent commits.
                    // For example, it changed from 'squid-autoconf-refactor' to 'autoconf-refactor'
                    // between r10147.1.21..r10147.1.22 and from 'memcache' to 'memcache-controls'
                    // between r9859.1.11..r9859.1.12.
                    // Adjust these branch names to correspond to the the same level.
                    var prevCommit = node.prev;
                    if (node.branchName != prevCommit.branchName)
                    {
                        if (branchNameDict.ContainsKey(prevCommit.branchName) &&
                            !branchNameDict.ContainsKey(node.branchName))
                        {
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
                                if (processedNodes.Contains(mergedFrom.id))
                                    continue;
                                if (!mergedFrom.finished())
                                    continue;
                                var newBranch = new Branch();
                                newBranch.end = mergedFrom;
                                nextLevelBranches.Add(newBranch);
                                //         branch.addChild(newBranch);
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
        void addCommit(Commit commit)
        {    
            StoreList.Add(commit);
        }

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
        #region === private and static data ===
        private static readonly HashSet<string> dirs = new HashSet<string>();
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
        #endregion
    }

    class MainClass
    {
        public static int Main(string[] args)
        {
            string fname = "";
            string ofname = "";
            if (args.Length > 0)
                fname = args[0];
            if (args.Length > 1)
                ofname = args[1];
            try
            {
                Adapter adapter = new Adapter();
                adapter.Adapt(fname, ofname);			    
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unhandled exception: {0}", ex.Message);
                Console.Error.WriteLine("Unhandled exception: {0}", ex.StackTrace);
                return 1;
            }
            return 0;
        }
    }
}

