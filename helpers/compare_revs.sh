#! /bin/sh

# Compares bzr against git Squid source trees for each of the given
# bzr revision and corresponding git sha.
# The 'sha rRevNo' pairs are provided via sha_revs_file, containing
# these lines, e.g.,:
# d2167ca720605f7b857c8ff75f1c5ecfe8c9823e r14000

sha_revs_file=/tmp/Squid/sha-revs-v4.txt
# a path a specific bzr branch: 
bzr_root=/tmp/Squid/bzr/v4
# a path to the converted git repository
git_root=/tmp/Squid/git

while read sha rev
do
  cd $git_root
  git reset --hard $sha
  cd $bzr_root
  bzr update -${rev}
  diff -r -x '.git' -x '.bzr' $bzr_root $git_root > /dev/null
  echo $? $rev >> ${sha_revs_file}.log
done < $sha_revs_file

