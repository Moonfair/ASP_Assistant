package aspa.mumu;

import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.regex.Pattern;

/** One selected Arknights process. Read only; stdin is a heartbeat, not a memory API. */
public final class BattleProbe {
    private static final long TYPE_SLOT = 0xcf2e378L, TYPE_TABLE = 0xd38b4b8L;
    private static final byte[] BUILD_ID = {
        0x77,0x70,0x02,0x71,(byte)0xce,(byte)0xe9,0x1e,0x0d,0x79,(byte)0xe4,
        (byte)0xe7,(byte)0x8f,0x30,0x2a,0x20,(byte)0xb6,(byte)0xaa,0x54,(byte)0xe6,(byte)0xf9
    };
    private static final Pattern KEY = Pattern.compile("[A-Za-z0-9_#.-]{1,110}");
    private final RandomAccessFile mem;
    private long[] previous;
    private long lastFrame, lastTicks;
    private int round;

    BattleProbe(RandomAccessFile mem) { this.mem = mem; }

    private static void check(boolean ok) throws IOException {
        if (!ok) throw new IOException("inconsistent battle");
    }
    private byte[] bytes(long p, int n) throws IOException {
        check(p >= 0x10000 && p < 0x800000000000L - n && n > 0 && n <= 2048);
        byte[] b = new byte[n];
        mem.seek(p);
        mem.readFully(b);
        return b;
    }
    private static ByteBuffer le(byte[] b) { return ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN); }
    private long word(long p) throws IOException { return le(bytes(p,8)).getLong(); }
    private int integer(long p) throws IOException { return le(bytes(p,4)).getInt(); }
    private static void pointer(long p) throws IOException {
        check(p >= 0x10000 && p < 0x800000000000L && (p & 7) == 0);
    }
    private void text(long p, String expected) throws IOException {
        byte[] want = (expected+"\0").getBytes(StandardCharsets.US_ASCII);
        check(Arrays.equals(want, bytes(p,want.length)));
    }
    private void klass(long k, String name, String ns) throws IOException {
        pointer(k); text(word(k+16),name); text(word(k+24),ns);
    }
    private void object(long p,String name,String ns) throws IOException {
        pointer(p); klass(word(p),name,ns);
    }
    private int listCount(byte[] header, int max) throws IOException {
        ByteBuffer b=le(header);
        int n=b.getInt(8);
        check(n>=0 && n<=max);
        if(n>0) {
            long arr=b.getLong(0); pointer(arr);
            long cap=word(arr+24); check(cap>=n && cap<=65536);
        }
        return n;
    }
    private String idle() { previous=null; return "idle"; }

    // Layout is fixed to the ELF build ID above. The four float slots and the
    // fixed-point clock deliberately match ArknightsBattleReader.ReadFrame.
    String sample(long k) throws IOException {
        klass(k,"BattleController","Torappu.Battle");
        long parent=word(k+88); klass(parent,"SingletonMonoBehaviour`1","Torappu");
        long slot=word(parent+184);
        if(slot==0) return idle();
        long bc=word(slot);
        if(bc==0) return idle();
        pointer(bc); check(word(bc)==k);
        long nativeObj=word(bc+16), logger=word(bc+224);
        if(nativeObj==0 || logger==0) return idle();
        object(logger,"BattleLogger","Torappu.Battle");
        check(word(nativeObj+40)==bc && word(logger+24)==bc);
        long stats=word(logger+48);
        if(stats==0) return idle();
        object(stats,"BattleStats","");
        long rows=word(stats+48);
        if(rows==0) return idle();
        object(rows,"ListDict`2","Torappu");
        long level=word(bc+352), clock=word(k+184); pointer(clock);
        byte[] tick=bytes(clock+20,12), head=bytes(rows+16,16);
        long frame=le(tick).getInt(0)&0xffffffffL, ticks=le(tick).getLong(4);
        int state=integer(bc+552), count=listCount(head,128);
        check(ticks>=0 && ticks<259200L*(1L<<32) && state>=0 && state<=3);
        StringBuilder values=new StringBuilder();
        Set<String> seen=new HashSet<>();
        if(count>0) {
            ByteBuffer pairs=le(bytes(le(head).getLong()+32,count*16));
            for(int i=0;i<count;i++) {
                long key=pairs.getLong(), row=pairs.getLong();
                object(key,"String","System"); int len=integer(key+16); check(len>0 && len<=110);
                String id=new String(bytes(key+20,len*2), StandardCharsets.UTF_16LE);
                check(KEY.matcher(id).matches());
                if(!id.startsWith("char_")) continue;
                check(seen.add(id));
                object(row,"CharAdvancedStats","");
                long list=word(row+56); object(list,"List`1","System.Collections.Generic");
                byte[] h=bytes(list+16,16); check(listCount(h,6)==6);
                ByteBuffer damage=le(bytes(le(h).getLong()+32,24));
                values.append('\t').append(id);
                for(int index : new int[]{1,2,3,5}) {
                    float v=damage.getFloat(index*4); check(v>=0 && v<=1e15f);
                    values.append(':').append(v);
                }
            }
        }
        check(word(slot)==bc && word(bc+16)==nativeObj && word(bc+224)==logger &&
            word(logger+24)==bc && word(logger+48)==stats && word(stats+48)==rows && word(bc+352)==level &&
            integer(bc+552)==state && Arrays.equals(head,bytes(rows+16,16)) && Arrays.equals(tick,bytes(clock+20,12)));
        long[] identity={bc,nativeObj,logger,stats,level};
        if(!Arrays.equals(previous,identity) || frame<lastFrame || ticks<lastTicks) round++;
        previous=identity; lastFrame=frame; lastTicks=ticks;
        return "frame\t"+round+"\t"+frame+"\t"+ticks+"\t"+(state==3?1:0)+values;
    }

    private long type(long base) throws IOException {
        long k=word(base+TYPE_SLOT);
        if((k&1)!=0) {
            long table=word(base+TYPE_TABLE);
            if(table==0) return 0;
            pointer(table); k=word(table+8L*10186);
        }
        return k;
    }
    // MuMu also maps a read-only copy at offset zero. Only use the ELF
    // mapping whose type slot lies inside a writable segment of the same file.
    private static long image(String proc,String pkg) throws IOException {
        List<Long> bases=new ArrayList<>(); List<long[]> writable=new ArrayList<>();
        try(BufferedReader maps=new BufferedReader(new FileReader(proc+"/maps"))) {
            String s;
            while((s=maps.readLine())!=null) {
                if(!s.endsWith("/lib/arm64/libil2cpp.so") || !s.contains("/"+pkg+"-")) continue;
                String[] cols=s.split("\\s+",6), range=cols[0].split("-");
                long lo=Long.parseLong(range[0],16), hi=Long.parseLong(range[1],16);
                if(Long.parseLong(cols[2],16)==0 && cols[1].startsWith("r")) bases.add(lo);
                if(cols[1].startsWith("rw")) writable.add(new long[]{lo,hi});
            }
        }
        long found=0;
        for(long b:bases) for(long[] r:writable)
            if(b+TYPE_SLOT>=r[0] && b+TYPE_SLOT+8<=r[1]) { check(found==0); found=b; }
        return found;
    }
    private static boolean matchesPackage(String proc,String pkg) throws IOException {
        try(InputStream in=new FileInputStream(proc+"/cmdline")) {
            for(byte b : (pkg+"\0").getBytes(StandardCharsets.US_ASCII)) if(in.read()!=(b&255)) return false;
            return true;
        }
    }
    private static boolean otherRunning(String pkg) throws IOException, InterruptedException {
        String other=pkg.endsWith(".bilibili") ? "com.hypergryph.arknights" : "com.hypergryph.arknights.bilibili";
        Process query=new ProcessBuilder("/system/bin/pidof",other).redirectErrorStream(true).start();
        try (InputStream result=query.getInputStream()) {
            int status=query.waitFor();
            check(status==0 || status==1);
            return status==0 && result.read()!=-1;
        } finally { query.destroy(); }
    }
    String poll(long base, boolean samePackage, boolean secondPackage) {
        if(!samePackage) return "absent";
        if(secondPackage) return "ambiguous";
        for(int i=0;i<3;i++) try {
            long k=type(base);
            return k==0?idle():sample(k);
        } catch(IOException changed) { /* retry the entire frame, not a partial row */ }
        return "retry";
    }
    public static void main(String[] args) throws Exception {
        if(args.length!=2 || !args[0].matches("[1-9][0-9]{0,8}") ||
            !(args[1].equals("com.hypergryph.arknights") || args[1].equals("com.hypergryph.arknights.bilibili"))) return;
        String proc="/proc/"+args[0], pkg=args[1];
        try {
            if(!matchesPackage(proc,pkg)) { System.out.println("absent"); return; }
            long base=image(proc,pkg);
            if(base==0) { System.out.println("boot"); return; }
            try(RandomAccessFile file=new RandomAccessFile(proc+"/mem","r")) {
                BattleProbe probe=new BattleProbe(file);
                if(!Arrays.equals(probe.bytes(base+0x3990,20), BUILD_ID)) { System.out.println("unsupported"); return; }
                // Host owns pacing. EOF (including adb disconnect) releases all resources.
                BufferedReader in=new BufferedReader(new InputStreamReader(System.in,StandardCharsets.US_ASCII));
                System.out.println("ready");
                while("poll".equals(in.readLine())) {
                    String result=probe.poll(base,matchesPackage(proc,pkg),otherRunning(pkg));
                    System.out.println(result);
                    if(result.equals("absent") || result.equals("ambiguous")) return;
                }
            }
        } catch(FileNotFoundException deniedOrExited) {
            System.out.println(new File(proc+"/cmdline").exists() ? "permission" : "absent");
        }
    }
}
