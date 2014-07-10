CUR="`pwd`"

ARCHIVER="$CUR/tools/7z/7z.exe"

DEBUG="/bin/Debug"
RELEASE="/bin/Release"

WORKERS_OUT="workers/"
WORKERS=("Janitor" "LoggerWorker" "ConfigProvider" "BrokerConsole" "MajordomoTestClient")

LIBS_OUT="libs/"
LIBS=("Majordomo.Client" "Majordomo" "EmailProvider")

BUILD_TYPE="$DEBUG"

rm -r "$LIBS_OUT"
rm -r "$WORKERS_OUT"

mkdir "$LIBS_OUT"
mkdir "$WORKERS_OUT"

for lib in ${LIBS[*]}; do
	ORIGIN="$lib$BUILD_TYPE/*"
	
	cp $ORIGIN $LIBS_OUT
done

for worker in ${WORKERS[*]}; do
	ORIGIN="$worker$BUILD_TYPE/*"
	DESTINATION="$WORKERS_OUT$worker"
	
	mkdir $DESTINATION
	
	cp -r $ORIGIN $DESTINATION
done

rm build.7z
"$ARCHIVER" a -t7z build.7z $WORKERS_OUT $LIBS_OUT