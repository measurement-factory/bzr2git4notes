#! /bin/sh

# This script automates exporting several Squid bzr branches
# into a single git repository.

# specify bzr and git repositories
export git_root=${PWD}/git_root3
export bzr_root=/home/ed/work/squid

# Specify bzr trunk path (it will be converted into git 'master'),
# it is currently v5.0. Other branches should be provided below when
# running import_branch().
export trunk_path=${bzr_root}/5.0

# the compiled bzr2git4notes should be in the current directory
export converter="mono bzr2git4notes.exe"

export GIT_DIR=${git_root}/.git

# (re)create git repository
if [ -d $git_root ]
then
  rm -rf $git_root
fi
mkdir $git_root
git init $git_root

import_branch()
{
    branch_name=$1
    branch_path=$2
    prev_branch_name=$3
    bzr fast-export --no-plain --import-marks=./marks.bzr -b $branch_name $branch_path |
        $converter --restore-context --git-export-file=./marks${prev_branch_name}.git |
        git fast-import --import-marks=./marks${prev_branch_name}.git --export-marks=./marks${branch_name}.git
}

# import bzr trunk first
bzr fast-export --no-plain --export-marks=./marks.bzr $trunk_path |
    $converter --store-context | git fast-import --export-marks=./marks.git

# import all other bzr branches
# this will import v3.5:
import_branch 3.5 ${bzr_root}/3.5
import_branch 4.0 ${bzr_root}/4.0 3.5
# add more branches if needed

