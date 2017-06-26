#! /bin/sh -e

# This script implements a single step of bzr-to-git conversion:
# Importing a single bzr branch into a git repository.
#
# The script assumes that the git "master" branch is the going to be the
# first branch in the git repository (and initializes the git repository
# when importing that master branch).
#
# Example usage:
#   bzr2git.sh ~/bazaar/repos/squid/5 /tmp/git/master
#   bzr2git.sh ~/bazaar/repos/squid/4 /tmp/git/v4
#   bzr2git.sh ~/bazaar/repos/squid/3.5 /tmp/git/v3.5
#   ...
#
# The tool generates several conversion state files (marks.git,
# marks.bzr and bzr2git4notes.bin). Keep them during the conversion!


# the compiled bzr2git4notes should be in the current directory
export converter="mono bzr2git4notes.exe"

usage()
{
    echo 'usage: bzr2git.sh <bzr_dir/branch_name> <git_dir/branch_name>'
}

source="$1"
destination="$2"

if [ -z "$source" ]
then
    usage
    echo "Missing bzr directory and branch name"
    exit 1
fi

if [ -z "$destination" ]
then
    usage
    echo "Missing git directory and branch name"
    exit 1
fi

if [ ! -z "$3" ]
then
    usage
    echo "Got more than two parameters"
    exit 1
fi

# git repository directory
export git_root=`dirname $destination`
# future git branch name
export git_branch=`basename $destination`

if [ ! -d "$source" ]
then
    usage
    echo "Expecting $source to be a directory"
    exit 1
fi

if [ ! -d "$source/.bzr" ]
then
    usage
    echo "Expecting $source to be a bzr checkout"
    exit 1
fi

if [ -e "$git_root" -a ! -d "$git_root/.git" ]
then
    usage
    echo "Expecting $destination to start with a git repository directory name but $git_root is not it"
    exit 1
fi

export GIT_DIR=${git_root}/.git

# create git repository (TODO: leave that up to the user?)
initRepository()
{
    if [ -d $git_root ]
    then
        echo "Expecting no git repository when importing master; found: $git_root"
        exit 1
    fi

    if [ -e marks.bzr ]
    then
        state="$state marks.bzr"
    fi

    if [ -e bzr2git4notes.bin ]
    then
        state="$state bzr2git4notes.bin"
    fi

    if [ -e marks.git ]
    then
        state="$state marks.git"
    fi

    if [ -n "$state" ]
    then
        echo "Expecting no old state files when importing master; found: $state"
        exit 1
    fi

    mkdir $git_root
    git init $git_root
}

mustExist()
{
    if [ ! -e "$1" ]
    then
        echo "$0: $1 does not exist!"
        exit 1
    fi
}

ret=0
if [ $git_branch = 'master' ]
then
    initRepository
    bzr fast-export --no-plain --export-marks=./marks.bzr $source |
        $converter --store-context |
        git fast-import --export-marks=./marks.git
    ret=$?

    mustExist marks.bzr
    mustExist bzr2git4notes.bin
    mustExist marks.git
else
    mustExist marks.bzr
    mustExist bzr2git4notes.bin
    mustExist marks.git

    bzr fast-export --no-plain --import-marks=./marks.bzr --export-marks=./marks_.bzr -b $git_branch $source |
        $converter --restore-context --store-context --git-export-file=./marks.git |
        git fast-import --import-marks=./marks.git --export-marks=./marks_.git
    ret=$?
    mv ./marks_.git ./marks.git
    mv ./marks_.bzr ./marks.bzr
fi

exit $ret
