package aspa.mumu;

import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

// Run with a desktop JDK. This is an artificial file, never a game process.
public final class BattleProbeTest {
    static RandomAccessFile f;
    static void bytes(long p, byte[] b) throws IOException { f.seek(p); f.write(b); }
    static void u64(long p, long n) throws IOException {bytes(p, ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).putLong(n).array());}
    static void i32(long p, int n) throws IOException {bytes(p, ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(n).array());}
    static void type(long k, String name, String ns) throws IOException {
        u64(k+16,k+0x200); u64(k+24,k+0x280);
        bytes(k+0x200,(name+"\0").getBytes("US-ASCII")); bytes(k+0x280,(ns+"\0").getBytes("US-ASCII"));
    }
    static void list(long p,long arr,int n) throws IOException {u64(p+16,arr); i32(p+24,n); u64(arr+24,n);}
    static void eq(String want,String got) { if(!want.equals(got)) throw new AssertionError(got+" != "+want); }
    public static void main(String[] args) throws Exception {
        File file=Files.createTempFile("aspa-battle", ".fixture").toFile();
        try (RandomAccessFile mem = new RandomAccessFile(file, "rw")) {
            f=mem;
            long k=0x10000, parent=0x11000, slot=0x12000, controller=0x13000, clock=0x14000;
            type(k,"BattleController","Torappu.Battle"); type(parent,"SingletonMonoBehaviour`1","Torappu");
            u64(k+88,parent);u64(k+184,clock);u64(parent+184,slot);u64(slot,0);
            BattleProbe probe=new BattleProbe(mem);
            eq("ambiguous",probe.poll(0,true,true)); // A was bound; a second package just started.
            eq("absent",probe.poll(0,false,false));
            eq("idle",probe.sample(k));
            long logger=0x15000, stats=0x16000, rows=0x17000, pairs=0x18000, key=0x19000;
            long row=0x1a000, types=0x1b000, arr=0x1c000, nativeObj=0x1d000;
            u64(slot,controller);u64(controller,k);u64(controller+16,nativeObj);u64(nativeObj+40,controller);
            u64(controller+224,logger);u64(logger+24,controller);u64(logger+48,stats);u64(stats+48,rows);
            type(0x20000,"BattleLogger","Torappu.Battle");u64(logger,0x20000);
            type(0x21000,"BattleStats","");u64(stats,0x21000);
            type(0x22000,"ListDict`2","Torappu");u64(rows,0x22000);
            type(0x23000,"String","System");u64(key,0x23000);
            type(0x24000,"CharAdvancedStats","");u64(row,0x24000);
            type(0x25000,"List`1","System.Collections.Generic");u64(types,0x25000);
            list(rows,pairs,1);u64(pairs+32,key);u64(pairs+40,row);
            i32(key+16,6);bytes(key+20,"char_a".getBytes(StandardCharsets.UTF_16LE));
            u64(row+56,types);list(types,arr,6);
            int[] values={999,10,20,30,900,40};
            for(int i=0;i<6;i++)i32(arr+32+i*4, Float.floatToIntBits(values[i]));
            i32(clock+20,30);u64(clock+24,4294967296L);i32(controller+552,3);
            eq("frame\t1\t30\t4294967296\t1\tchar_a:10.0:20.0:30.0:40.0",probe.sample(k));
            i32(clock+20,0);u64(clock+24,0);i32(controller+552,1);
            eq("frame\t2\t0\t0\t0\tchar_a:10.0:20.0:30.0:40.0",probe.sample(k));
            u64(logger+24,0);
            try {probe.sample(k); throw new AssertionError("foreign logger accepted");} catch(IOException expected) {}
            u64(slot,0);eq("idle",probe.sample(k));
            System.out.println("PASS idle, four counters, clock, ended, round rollover, ownership");
        } finally { Files.delete(file.toPath()); }
    }
}
