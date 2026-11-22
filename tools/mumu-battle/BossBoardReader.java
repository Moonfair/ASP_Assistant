package aspa.mumu;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import static aspa.mumu.BattleProbe.check;
import static aspa.mumu.BattleProbe.le;

/** Optional leaderboard of the selected game only. No nickname, account or roster reads. */
final class BossBoardReader {
    private static final String CHESS="Torappu.Battle.AutoChess", CENTER="Torappu.Battle.DataCenter",
        GENERIC="System.Collections.Generic";
    private final BattleProbe p;
    BossBoardReader(BattleProbe p) { this.p=p; }

    String suffix(long controller) {
        try {
            String first=stage(controller);
            return first.isEmpty() || first.equals(stage(controller)) ? first : "";
        } catch(IOException unavailable) { return ""; } // Never discard a valid local battle frame.
    }
    private long child(long parent,int offset,String type,String ns) throws IOException {
        long obj=p.word(parent+offset); p.object(obj,type,ns); return obj;
    }
    // Validate the managed field's name and declaring class before using its ARM64 offset.
    // In particular, a plausible integer alone is not evidence that it is boss damage.
    private int namedInt(long obj,int ordinal,String name) throws IOException {
        long klass=p.word(obj), fields=p.word(klass+128), info=fields+ordinal*32L;
        check(p.word(info+16)==klass); p.text(p.word(info),name);
        int offset=p.integer(info+24); check(offset>=16 && offset<=1024 && (offset&3)==0);
        return p.integer(obj+offset);
    }
    private String key(long obj) throws IOException {
        p.object(obj,"String","System"); int length=p.integer(obj+16); check(length>0 && length<=128);
        String s=new String(p.bytes(obj+20,length*2),StandardCharsets.UTF_16LE);
        for(int i=0;i<s.length();i++) {
            char c=s.charAt(i);
            check(c>='a'&&c<='z' || c>='A'&&c<='Z' || c>='0'&&c<='9' || c=='_' || c=='-');
        }
        return s;
    }
    private long manager(long controller) throws IOException {
        child(controller,376,"AutoChessGameMode","");
        long host=child(controller,744,"SingletonHost","");
        long dict=child(host,16,"Dictionary`2",GENERIC);
        int count=p.integer(dict+32); check(count>0 && count<=80);
        long array=p.word(dict+24); check(p.word(array+24)>=count);
        ByteBuffer entries=le(p.bytes(array+32,count*24));
        for(int i=0;i<count*24;i+=24) {
            long value=entries.getLong(i+16);
            if(entries.getInt(i)<0 || entries.getLong(i+8)==0 || value==0) continue;
            try {p.object(value,"AutoChessGameManager",CHESS); return value;} catch(IOException other) {}
        }
        throw new IOException("No garrison manager");
    }
    private String stage(long controller) throws IOException {
        long manager=manager(controller), status=child(manager,56,"AutoChessGameStatus",CHESS);
        long model=child(status,16,"AutoChessGameStateModel","");
        long machine=child(model,24,"AutoChessGameStateMachine","");
        long current=child(machine,96,"AutoChessGameState","");
        int state=namedInt(current,2,"m_state"); check(state>=0 && state<=14 && state==p.integer(machine+92));
        long center=child(manager,64,"AutoChessDataCenter",CENTER);
        int round=p.integer(center+60), self=p.integer(center+56); check(round>=0 && self>=0 && self<32);
        long id=p.word(center+16);
        String head="|"+state+","+round+","+self+","+(id==0?"":key(id))+",";
        if(state!=6) return head+"0";
        try {return head+board(center);} catch(IOException unavailable) {return head+"0";}
    }
    private String board(long center) throws IOException {
        int hp=0;
        try {
            if(p.word(center+136)!=0)
                hp=Math.max(namedInt(child(center,136,"BossBattleData",CHESS),0,"bossHp"),0);
        } catch(IOException unavailable) { /* A missing denominator is not zero damage. */ }
        long status=child(center,240,"AutoChessBattleStatusDataModel",CHESS);
        long list=child(status,72,"List`1",GENERIC);
        byte[] header=p.bytes(list+16,16); int count=p.listCount(header,256);
        if(count==0) return ""+hp;
        ByteBuffer items=le(p.bytes(le(header).getLong()+32,count*8));
        // Keep individual entries (even if two players use the same chess ID) and source tie order.
        int[][] top=new int[3][];
        for(int i=0;i<count;i++) {
            long item=items.getLong(); p.object(item,"AutoChessBossDamageDataItemModel",CHESS);
            int owner=namedInt(item,0,"uidIndex"), chess=namedInt(item,1,"chessId"), damage=namedInt(item,2,"damage");
            check(owner>=0 && owner<32 && chess>=0 && damage>=0);
            int[] entry={owner,chess,damage};
            for(int j=0;j<3;j++) if(top[j]==null || entry[2]>top[j][2]) {
                int[] old=top[j]; top[j]=entry; entry=old; if(entry==null)break;
            }
        }
        check(Arrays.equals(header,p.bytes(list+16,16)));
        String[] names=new String[3];
        try {resolveChessNames(center,top,names);} catch(IOException unavailable) { /* retain numeric IDs */ }
        StringBuilder result=new StringBuilder().append(hp);
        for(int i=0;i<3 && top[i]!=null;i++) result.append(';').append(top[i][0]).append(',')
            .append(names[i]!=null?names[i]:Integer.toString(top[i][1])).append(',').append(top[i][2]);
        return result.toString();
    }
    private void resolveChessNames(long center,int[][] top,String[] names) throws IOException {
        long index=child(center,296,"AutoChessDataIndexer",CENTER),dict=child(index,48,"Dictionary`2",GENERIC);
        int count=p.integer(dict+32); check(count>=0 && count<=4096); if(count==0)return;
        long array=p.word(dict+24); check(p.word(array+24)>=count);
        for(int first=0;first<count;first+=80) {
            int size=Math.min(80,count-first); ByteBuffer block=le(p.bytes(array+32+first*24L,size*24));
            for(int j=0;j<size*24;j+=24) {
                if(block.getInt(j)<0 || block.getLong(j+16)==0)continue;
                for(int i=0;i<3 && top[i]!=null;i++) if(block.getInt(j+8)==top[i][1])
                    names[i]=key(block.getLong(j+16));
            }
        }
    }
}
