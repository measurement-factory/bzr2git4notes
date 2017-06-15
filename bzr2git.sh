#! /bin/sh

# This script automates exporting a Squid bzr branch
# into a git repository. Before running it, please
# specify directories for bzr and git repositories (below).
#
# The initial step initializes git repository and imports
# bzr 'trunk' (or v5.0) into git 'master':
#
# bzr2git.sh --bzr-branch 5.0 --git-branch master
#
# After that, it is possible to import all other required branches,
# for example:
#
# bzr2git.sh --bzr-branch 4.0 --git-branch 4.0
# bzr2git.sh --bzr-branch 3.5 --git-branch 3.5
# ...
#
# The tool generates several context files (marks.git, marks.bzr and
# bzr2git4notes.bin), used for importing multiple branches.


# Please specify git repository directory.
# It will be initialized before importing 'master'.
export git_root=${PWD}/git_root

# Please specify bzr repository directory.
# All exporting bzr branches should be here, within
# corresponding sub-directories.
export bzr_root=${PWD}/bzr_root

# the compiled bzr2git4notes should be in the current directory
export converter="mono bzr2git4notes.exe"

export GIT_DIR=${git_root}/.git

# parse command line
while [ $# -gt 1 ]
do
key="$1"
case $key in
    -b|--bzr-branch)
    bzr_branch="$2"
    shift
    ;;
    -g|--git-branch)
    git_branch="$2"
    shift
    ;;
    *)
    ;;
esac
shift
done

if [ -z $git_branch ] || [ -z $bzr_branch ]
then
    echo 'usage: bzr2git.sh -b <bzr_branch> -g <git_branch>'
fi

# (re)create git repository
initRepository()
{
    if [ -d $git_root ]
    then
      rm -rf $git_root
    fi
    mkdir $git_root
    git init $git_root
}

ret=0

checkCreated()
{
  if [ -e $1 ]
  then
     echo bzr2git: $1 successfully created.
  else
     echo bzr2git: $1 creation failure!
     ret=1
  fi
}

check()
{
  if [ ! -e $1 ]
  then
     echo bzr2git: $1 does not exist!
     exit 1
  fi
}

if [ $git_branch = 'master' ]
then
    initRepository
    bzr fast-export --no-plain --export-marks=./marks.bzr ${bzr_root}/${bzr_branch} |
        $converter --store-context | git fast-import --export-marks=./marks.git
    ret=$?
    checkCreated marks.bzr
    checkCreated bzr2git4notes.bin
    checkCreated marks.git
else
    check marks.bzr
    check bzr2git4notes.bin
    check marks.git

    bzr fast-export --no-plain --import-marks=./marks.bzr -b $git_branch ${bzr_root}/${bzr_branch} |
        $converter --restore-context --git-export-file=./marks.git |
        git fast-import --import-marks=./marks.git --export-marks=./marks_.git
    ret=$?
    mv ./marks_.git ./marks.git
fi

exit $ret

