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
    static void obj(long p,long k,String name,String ns) throws IOException {type(k,name,ns);u64(p,k);}
    static void field(long k,int ord,String name,int offset) throws IOException {
        long table=k+0x400, s=k+0x800+64*ord, info=table+32*ord;
        u64(k+128,table);u64(info,s);u64(info+16,k);i32(info+24,offset);
        bytes(s,(name+"\0").getBytes("US-ASCII"));
    }
    static void str(long p,String value) throws IOException {
        u64(p,0x23000);i32(p+16,value.length());bytes(p+20,value.getBytes(StandardCharsets.UTF_16LE));
    }
    static void bossFixture(long bc) throws IOException {
        String chess="Torappu.Battle.AutoChess", generic="System.Collections.Generic";
        u64(bc+376,0x30000);obj(0x30000,0x50000,"AutoChessGameMode","");
        u64(bc+744,0x31000);obj(0x31000,0x51000,"SingletonHost","");
        u64(0x31010,0x32000);obj(0x32000,0x52000,"Dictionary`2",generic);
        u64(0x32018,0x33000);i32(0x32020,1);u64(0x33018,1);u64(0x33028,0x30000);u64(0x33030,0x34000);
        obj(0x34000,0x53000,"AutoChessGameManager",chess);u64(0x34038,0x35000);u64(0x34040,0x39000);
        obj(0x35000,0x54000,"AutoChessGameStatus",chess);u64(0x35010,0x36000);
        obj(0x36000,0x55000,"AutoChessGameStateModel","");u64(0x36018,0x37000);
        obj(0x37000,0x56000,"AutoChessGameStateMachine","");i32(0x3705c,6);u64(0x37060,0x38000);
        obj(0x38000,0x57000,"AutoChessGameState","");i32(0x38014,6);field(0x57000,2,"m_state",20);
        obj(0x39000,0x58000,"AutoChessDataCenter","Torappu.Battle.DataCenter");
        i32(0x39038,1);i32(0x3903c,14);u64(0x39010,0x41000);str(0x41000,"act3autochess");
        u64(0x39088,0x3a000);obj(0x3a000,0x59000,"BossBattleData",chess);i32(0x3a010,1000000);field(0x59000,0,"bossHp",16);
        u64(0x390f0,0x3b000);obj(0x3b000,0x5a000,"AutoChessBattleStatusDataModel",chess);
        u64(0x3b048,0x3c000);u64(0x3c000,0x25000);list(0x3c000,0x3d000,1);u64(0x3d020,0x3e000);
        obj(0x3e000,0x5b000,"AutoChessBossDamageDataItemModel",chess);
        i32(0x3e010,2);i32(0x3e014,111);i32(0x3e018,69000);
        field(0x5b000,0,"uidIndex",16);field(0x5b000,1,"chessId",20);field(0x5b000,2,"damage",24);
        u64(0x39128,0x3f000);obj(0x3f000,0x5c000,"AutoChessDataIndexer","Torappu.Battle.DataCenter");
        u64(0x3f030,0x40000);u64(0x40000,0x52000);u64(0x40018,0x43000);i32(0x40020,1);
        u64(0x43018,1);i32(0x43028,111);u64(0x43030,0x42000);str(0x42000,"chess_char_5_22_a");
    }
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
            String global="frame\t2\t0\t0\t0\tchar_a:10.0:20.0:30.0:40.0";
            bossFixture(controller);
            eq(global+"|6,14,1,act3autochess,1000000;2,chess_char_5_22_a,69000",probe.sample(k));
            field(0x59000,0,"notBossHp",16);
            eq(global+"|6,14,1,act3autochess,0;2,chess_char_5_22_a,69000",probe.sample(k));
            field(0x59000,0,"bossHp",16);
            field(0x5b000,2,"notDamage",24);
            eq(global+"|6,14,1,act3autochess,0",probe.sample(k));
            field(0x5b000,2,"damage",24);
            i32(0x3e018,-1);
            eq(global+"|6,14,1,act3autochess,0",probe.sample(k));
            i32(0x3705c,4);i32(0x38014,4);
            eq(global+"|4,14,1,act3autochess,0",probe.sample(k)); // stale boss table is ignored
            i32(0x38014,3);
            eq(global,probe.sample(k)); // torn phase never breaks the global feed
            u64(logger+24,0);
            try {probe.sample(k); throw new AssertionError("foreign logger accepted");} catch(IOException expected) {}
            u64(slot,0);eq("idle",probe.sample(k));
            System.out.println("PASS idle, four counters, clock, round rollover, boss damage/HP/owner, non-boss, corrupt board and torn phase");
        } finally { Files.delete(file.toPath()); }
    }
}
