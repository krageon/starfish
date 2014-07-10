#!/bin/bash
# An automatic release builder for VS solutions using msbuild, a bash shell (ie msysgit) and GnuWin32

# A POSIX variable
OPTIND=1         # Reset in case getopts has been used previously in the shell.

# Constants
CUR="`pwd`"
DELIMITER="/"

# Destination folder
DESTINATION="./release"
DESTINATION_FULL="$CUR$DELIMITER$DESTINATION"

# Solution information
ORIGIN="taskserver"
SOLUTION=$ORIGIN".sln"

# Build information
PLATFORM="Any CPU"
NUGET="tools/nuget.exe"

CLIENT="Majordomo.Client"
MAIN="Majordomo"
EMAIL_PROVIDER="EmailProvider"

BIN="/bin/" 

OUT="/libs/"

# Default values
buildconfig="Release"
MSBUILD="msbuild.exe"

TESTME=1

LOGFOLDER="$CUR/log/"

TESTRUNNER="$CUR/tools/TestWindow/vstest.console.exe"
TEST_LOG_BEGIN="$LOGFOLDER/test_"
TEST_EXTENSION=".log"

TEST_BEGIN="$CUR/"
TEST_EXTENSION=".dll"

TEST=("Majordomo.Broker.Test" "Janitor.Test")

function findmsbuild {
	echo "Attempting to find msbuild"
	# This no longer happens because we want the latest msbuild version
	#	the one in path is not guaranteed to be the latest
	#msbuildeval=$(cmd -/C "$MSBUILD /?" && echo "ok" || echo "")
	#if [ -n "$msbuildeval" ]; then
	#	echo "Success, found in PATH."
	#	return
	#fi
	
	msbuildloc=$(cmd -/C "reg query 	$(echo "HKLM\\SOFTWARE\\Microsoft\\MSBuild\\ToolsVersions") /s /v MSBuildToolsPath" | grep REG_SZ | awk '{print $NF}' | tail -1)
	if [[ -z "$msbuildloc" ]]; then
		echo "msbuild could not be found in registry or path - please install .NET framework or fix what is broken"
		exit 1
	fi
	
	echo "Success: found in registry. Located at:"
	MSBUILD=$msbuildloc$MSBUILD
	echo $MSBUILD
}

function nuget_restore {
	echo "Restoring NuGet packages"
	$NUGET restore $SOLUTION
}

function clean_solution {
	echo "Cleaning solution using git's gitignore file"
	# -d = delete whole directories, -f = force delete, -X = remove only ignored files
	git clean -dfX > clean.log 2>&1
}

function build_solution {
	echo "Rebuilding $SOLUTION in $buildconfig configuration."
	# Switched to msbuild for faster building and being separate from visual studio
	#		msbuild should always be available to cmd if .NET is installed
	echo "$MSBUILD $SOLUTION /t:ReBuild /p:Configuration=$buildconfig"
	cmd -/C "$MSBUILD $SOLUTION /t:ReBuild /p:Configuration=$buildconfig" > build.log 2>&1
	
	RES=`tail -3 build.log | head -n 1 | sed 's/^ *//'`
	if [ "$RES" == "0 Error(s)" ]; then
		echo "Built OK"
	else
		echo ''
		echo $RES
		echo -e "\e[41mBuild failed, see build.log for details. build.log tail:\e[0m"
		tail build.log
		exit 1
	fi
}

function run_tests {
	echo "Running $buildconfig tests for project"
	
	echo ''
	# This runs the test using a copied over self-sustaining piece of visual studio called
	#	VSTest.Console.exe.
	
	rm -r "$LOGFOLDER"
	mkdir "$LOGFOLDER"
	
	TEST_BIN="/bin/$buildconfig/"
	
	for testname in ${TEST[*]}; do
		echo "Testing $testname"
		TESTSUBJECT="$TEST_BEGIN$testname$TEST_BIN$testname$TEST_EXTENSION"
		TESTRESULT="$TEST_LOG_BEGIN$testname$TEST_EXTENSION"
		
		"$TESTRUNNER" "$TESTSUBJECT" >> "$TESTRESULT" 2>&1

		# This takes the last two lines of that file, and takes the first line of that
		RES=`tail -2 "$TESTRESULT" | head -n 1`
		if [ "$RES" == "Test Run Successful." ]; then
			echo "Tests ran OK"
		else
			echo ''
			echo -e "\e[41mTests failed, see log/test.log for details. test.log tail:\e[0m"
			tail "$TESTRESULT"
			exit 1
		fi
	done
}

function copy_to_libs
{
	cd "$CUR"
	
	mkdir libs
	
	echo "Cleaning libs"
	rm -rf libs/client
	rm -rf libs/majordomo
	
	mkdir libs/client
	mkdir libs/majordomo
	
	echo "moving output files to libs folder"
	mv ./$MAIN$BIN$buildconfig/* .$OUT/majordomo
	mv ./$CLIENT$BIN$buildconfig/* .$OUT/client
	mv ./$EMAIL_PROVIDER$BIN$buildconfig/* .$OUT/majordomo
}

function build
{
	findmsbuild
	echo ''
	clean_solution
	echo ''
	nuget_restore
	echo ''
	build_solution
	echo "Built solution using $buildconfig configuration"
}

function build_release
{
	build
	echo ''
	copy_to_libs
	echo 'Done.'
}

function display_help
{
	cat << EOF
Build system

---------------
    Usage
---------------

	h shows the help
	d builds for debug (using .gitbuildignoredebug). 
		By default this includes all .pdb files.
	
	b performs a build only
	f builds and copies output to libs
	
	invoking without arguments builds as if only -f was passed

---------------
    Examples
---------------

Creating a release for live:
	./release.sh
	./release.sh -f
	
Build only:
	./release.sh -b
	
Creating a release with debug information:
	./release.sh -df

Note that d comes before a build option (ie f or b).
If you do not do this, d will be ignored.
EOF
# The EOF stuff needs to be exactly like this so it'll spit out a string exactly as between the tags

}

# Parse potential flags
while getopts "dhbft" opt; do
    case "$opt" in
    d)  
		echo "Setting debug as build target"
		buildconfig="Debug"
		echo ''
		if [ $OPTIND -eq 2 ]; then
			echo 'WARNING: d comes before f or b, or it will be ignored!'
		fi
        ;;
	h)	
		display_help
		;;
	b)
		echo "Performing a build only"
		echo ''
		build
		;;
	f)
		echo "Performing release build"
		echo ''
		build_release
		;;
	t)
		echo ''
		run_tests
		;;
	*) # This is the default (ie options were given but not recognised)
		display_help
		;;
    esac
done

# This means build_release gets called if no options were supplied
if [ $OPTIND -eq 1 ]; then 
	build_release
fi

exit 0